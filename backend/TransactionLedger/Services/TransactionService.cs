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
