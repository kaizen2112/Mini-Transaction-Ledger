using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TransactionLedger.Data;
using TransactionLedger.Domain;
using TransactionLedger.DTOs;

namespace TransactionLedger.Services;

public sealed class TransactionService : ITransactionService
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditService _auditService;

    public TransactionService(AppDbContext dbContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _auditService = auditService;
    }

    /// <summary>
    /// The order of the steps below is the whole point of this method.
    ///
    ///   1. open an explicit database transaction        (BR-19)
    ///   2. SELECT the account FOR UPDATE, with ownership
    ///      in the predicate                             (BR-29, BR-07)
    ///   3. check the overdraft rule if this is a debit  (BR-20)
    ///   4. insert the transaction row
    ///   5. update the stored balance                    (BR-17)
    ///   6. write an audit entry                         (BR-36)
    ///   7. commit
    ///
    /// Without step 2 this method loses updates. Two concurrent debits of 60
    /// against a balance of 100 both read 100, both decide 100 >= 60, both
    /// write 40, and the account has paid out 120 while showing 40. The lock
    /// makes the second reader wait until the first commits, so it reads 40
    /// and correctly refuses. Steps 4-6 share one commit so a crash between
    /// them cannot leave a transaction row without its balance change, which
    /// would violate BR-17 permanently and silently.
    /// </summary>
    public async Task<TransactionResponse> CreateAsync(
        Guid userId,
        Guid accountId,
        CreateTransactionRequest request,
        CancellationToken cancellationToken)
    {
        // 1. Explicit transaction. Everything below either commits together or
        //    rolls back together (BR-19).
        await using var databaseTransaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // 2. Exclusive row lock, with ownership as part of the predicate
        //    (BR-07/BR-29). A foreign or missing account yields no row, so
        //    "not yours" and "does not exist" are the same outcome (BR-08).
        //    The lock is held until commit.
        var account = await LockAccountAsync(userId, accountId, cancellationToken);

        if (account is null)
        {
            throw NotFound();
        }

        // 3. Overdraft check, AFTER the lock. Before the lock the balance read
        //    would be stale the instant another writer committed (BR-20).
        if (account.WouldOverdraw(request.Type, request.Amount))
        {
            throw new DomainException(
                ErrorCodes.InsufficientFunds,
                StatusCodes.Status409Conflict,
                // The contract requires both figures so the UI can render a
                // useful message without a second call.
                $"Account balance is {account.Balance:0.00}; requested debit is {request.Amount:0.00}.");
        }

        // 4. Insert. The domain factory applies BR-03/04/05/24/25 and sets
        //    OccurredAt from the server clock (BR-26).
        var transaction = Transaction.Record(
            accountId,
            request.Type,
            request.Amount,
            request.Category,
            request.Description);

        _dbContext.Transactions.Add(transaction);

        // 5. Update the stored balance (BR-17). Safe because the row lock from
        //    step 2 is still held.
        account.Apply(transaction);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // 6. Audit, inside the same transaction (BR-36). Metadata deliberately
        //    does not duplicate the financial columns (BR-37).
        await _auditService.RecordAsync(
            userId,
            AuditActions.TransactionCreated,
            nameof(Transaction),
            transaction.Id,
            new { transaction.AccountId, Type = transaction.Type.ToString() },
            cancellationToken);

        // 7. Commit. The lock is released here.
        await databaseTransaction.CommitAsync(cancellationToken);

        return Map(transaction, isReversed: false);
    }

    /// <summary>
    /// GET /api/accounts/{accountId}/transactions (contract §5).
    ///
    /// Every filter, the ordering, the count and the page slice execute in
    /// PostgreSQL (BR-40). Nothing is materialised before Skip/Take — an
    /// in-memory Skip still transfers every row over the wire, which works
    /// with 50 rows and dies with 500,000.
    /// </summary>
    public async Task<PagedResponse<TransactionResponse>> ListAsync(
        Guid userId,
        Guid accountId,
        TransactionQuery query,
        CancellationToken cancellationToken)
    {
        query.Validate();

        // BR-08: a foreign or missing account is a 404 before any history is
        // exposed. Checked separately from the transaction query so that an
        // owned account with no transactions still returns 200 with an empty
        // page — an empty result and "not yours" must not look the same.
        var ownsAccount = await _dbContext.Accounts
            .AsNoTracking()
            .AnyAsync(a => a.Id == accountId && a.UserId == userId, cancellationToken);

        if (!ownsAccount)
        {
            throw NotFound();
        }

        var filtered = ApplyFilters(
            _dbContext.Transactions.AsNoTracking().Where(t => t.AccountId == accountId),
            query);

        // COUNT(*) over the filtered set, still in SQL.
        var totalItems = await filtered.CountAsync(cancellationToken);

        var items = await Project(OrderForHistory(filtered))
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return PagedResponse<TransactionResponse>.Create(
            items,
            query.Page,
            query.PageSize,
            totalItems);
    }

    /// <summary>
    /// GET /api/transactions/{transactionId} (contract §5). Ownership is
    /// checked through the join to Accounts (BR-07) — the predicate, not a
    /// post-load check.
    /// </summary>
    public async Task<TransactionResponse> GetAsync(
        Guid userId,
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        var owned = from transaction in _dbContext.Transactions.AsNoTracking()
                    join account in _dbContext.Accounts.AsNoTracking()
                        on transaction.AccountId equals account.Id
                    where transaction.Id == transactionId && account.UserId == userId
                    select transaction;

        var result = await Project(owned).SingleOrDefaultAsync(cancellationToken);

        return result ?? throw NotFound();
    }

    /// <summary>BR-42. Each filter is applied only when supplied.</summary>
    private static IQueryable<Transaction> ApplyFilters(IQueryable<Transaction> source, TransactionQuery query)
    {
        if (query.Type.HasValue)
        {
            source = source.Where(t => t.Type == query.Type.Value);
        }

        if (query.Category.HasValue)
        {
            source = source.Where(t => t.Category == query.Category.Value);
        }

        // Inclusive UTC bounds on OccurredAt.
        if (query.From.HasValue)
        {
            source = source.Where(t => t.OccurredAt >= query.From.Value);
        }

        if (query.To.HasValue)
        {
            source = source.Where(t => t.OccurredAt <= query.To.Value);
        }

        if (query.MinAmount.HasValue)
        {
            source = source.Where(t => t.Amount >= query.MinAmount.Value);
        }

        if (query.MaxAmount.HasValue)
        {
            source = source.Where(t => t.Amount <= query.MaxAmount.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // EF.Functions.ILike maps to PostgreSQL ILIKE, so the match is
            // case-insensitive in the database rather than by pulling rows
            // into memory. It cannot use a B-tree index; documented and
            // accepted at this scale (BR-42, NFR-04).
            var pattern = $"%{query.Search.Trim()}%";
            source = source.Where(t => t.Description != null && EF.Functions.ILike(t.Description, pattern));
        }

        return source;
    }

    /// <summary>
    /// BR-41: always OccurredAt DESC, Id DESC. The tiebreak on Id is what
    /// makes the ordering total — two rows in the same millisecond would
    /// otherwise have undefined relative order, so one could appear on both
    /// page 1 and page 2, or on neither. This matches
    /// IX_Transactions_Account_Occurred exactly, so the page is an index scan
    /// with no sort node.
    /// </summary>
    private static IQueryable<Transaction> OrderForHistory(IQueryable<Transaction> source) =>
        source.OrderByDescending(t => t.OccurredAt).ThenByDescending(t => t.Id);

    /// <summary>
    /// IsReversed is computed by the query, never stored, because the original
    /// row is never mutated (BR-21). This becomes an EXISTS against the
    /// partial unique index UX_Transactions_Reverses.
    /// </summary>
    private IQueryable<TransactionResponse> Project(IQueryable<Transaction> source) =>
        source.Select(t => new TransactionResponse(
            t.Id,
            t.AccountId,
            t.Type,
            t.Amount,
            t.Category,
            t.Description,
            t.OccurredAt,
            t.ReversesTransactionId,
            _dbContext.Transactions.Any(r => r.ReversesTransactionId == t.Id),
            t.TransferId));

    /// <summary>
    /// BR-29: issued as raw SQL because FOR UPDATE has no LINQ equivalent.
    ///
    /// ToListAsync rather than SingleOrDefaultAsync on purpose: the latter
    /// composes a LIMIT over the raw SQL, which wraps it in a subquery, and
    /// PostgreSQL rejects FOR UPDATE inside a subquery. The predicate already
    /// filters on a primary key, so at most one row can come back.
    ///
    /// Tracked, not AsNoTracking: step 5 mutates this instance.
    /// </summary>
    private async Task<Account?> LockAccountAsync(
        Guid userId,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var locked = await _dbContext.Accounts
            .FromSql(
                $"""
                 SELECT * FROM "Accounts"
                 WHERE "Id" = {accountId} AND "UserId" = {userId}
                 FOR UPDATE
                 """)
            .ToListAsync(cancellationToken);

        return locked.SingleOrDefault();
    }

    /// <summary>
    /// BR-08: identical for a foreign account and one that does not exist. The
    /// detail names no identifier, or the two responses would differ.
    /// </summary>
    private static DomainException NotFound() => new(
        ErrorCodes.NotFound,
        StatusCodes.Status404NotFound,
        "The requested resource was not found.");

    private static TransactionResponse Map(Transaction transaction, bool isReversed) =>
        new(
            transaction.Id,
            transaction.AccountId,
            transaction.Type,
            transaction.Amount,
            transaction.Category,
            transaction.Description,
            transaction.OccurredAt,
            transaction.ReversesTransactionId,
            isReversed,
            transaction.TransferId);
}
