using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TransactionLedger.Data;
using TransactionLedger.Domain;
using TransactionLedger.DTOs;

namespace TransactionLedger.Services;

public sealed class TransferService : ITransferService
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditService _auditService;

    public TransferService(AppDbContext dbContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _auditService = auditService;
    }

    /// <summary>
    /// POST /api/transfers (contract §6).
    ///
    /// The step order is the whole method, exactly as in TransactionService but
    /// with two accounts instead of one:
    ///
    ///   0. reject same-account and bad amounts BEFORE any lock   (BR-27, BR-03)
    ///   1. open one explicit database transaction                (BR-19, BR-28)
    ///   2. lock BOTH accounts, ORDER BY "Id" FOR UPDATE          (BR-29, BR-30)
    ///   3. both rows present? if not, 404                        (BR-07, BR-08)
    ///   4. overdraft check on the source                         (BR-20)
    ///   5. insert the debit and credit legs
    ///   6. insert the Transfer row
    ///   7. backfill TransferId on both legs                      (docs/04 §3.4)
    ///   8. update both balances                                  (BR-17)
    ///   9. audit                                                 (BR-36)
    ///  10. commit — one commit, so all of it or none of it       (BR-28)
    ///
    /// Step 2 is where this differs from a single credit or debit. Locking two
    /// rows introduces deadlock, which locking one cannot have: transfer A→B
    /// holding A and waiting for B, while B→A holds B and waits for A, is a
    /// cycle neither can escape. PostgreSQL detects it and kills one — not
    /// corruption, but a random user-visible failure. ORDER BY "Id" removes the
    /// possibility rather than handling it: every transfer touching the same
    /// pair queues for the same row first, so no cycle can form (BR-30).
    ///
    /// Note there is no retry loop anywhere here. That is the point of choosing
    /// pessimistic locking (ADR 0001): the second writer waits and then reads
    /// the committed balance, instead of failing and needing a retry policy
    /// that would itself need idempotency to be already correct.
    /// </summary>
    public async Task<TransferResponse> CreateAsync(
        Guid userId,
        CreateTransferRequest request,
        CancellationToken cancellationToken)
    {
        // 0. Cheap request-shape checks first. Both are pure functions of the
        //    payload, so discovering them after taking exclusive locks on two
        //    accounts would be wasted contention.
        if (request.SourceAccountId == request.DestinationAccountId)
        {
            // BR-27. Checked before ownership on purpose: this is a malformed
            // request regardless of who owns the account, and CK_Transfers_
            // DifferentAccounts is the database's backstop for the same rule.
            throw new DomainException(
                ErrorCodes.SameAccountTransfer,
                StatusCodes.Status400BadRequest,
                "Source and destination accounts must be different.");
        }

        Transaction.ValidateAmount(request.Amount);

        // 1. One transaction around everything below (BR-19, BR-28).
        await using var databaseTransaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // 2. Both accounts locked in one statement, in ascending ID order
        //    (BR-30), with ownership in the predicate (BR-07).
        var locked = await LockAccountsAsync(
            userId,
            request.SourceAccountId,
            request.DestinationAccountId,
            cancellationToken);

        // 3. Fewer than two rows means at least one account is missing or
        //    belongs to someone else — indistinguishable by design (BR-08).
        var source = locked.SingleOrDefault(a => a.Id == request.SourceAccountId);
        var destination = locked.SingleOrDefault(a => a.Id == request.DestinationAccountId);

        if (source is null || destination is null)
        {
            throw NotFound();
        }

        // 4. Overdraft check, AFTER the lock. Before it the balance would be
        //    stale the instant another writer committed (BR-20).
        if (source.WouldOverdraw(TransactionType.Debit, request.Amount))
        {
            throw new DomainException(
                ErrorCodes.InsufficientFunds,
                StatusCodes.Status409Conflict,
                $"Account balance is {source.Balance:0.00}; requested transfer is {request.Amount:0.00}.");
        }

        // 5. Two real ledger entries, not a bookkeeping shortcut (BR-27). One
        //    timestamp read, shared by both legs, so they cannot sort apart.
        var occurredAt = DateTime.UtcNow;

        var debitLeg = Transaction.RecordTransferLeg(
            source.Id,
            TransactionType.Debit,
            request.Amount,
            request.Description,
            occurredAt);

        var creditLeg = Transaction.RecordTransferLeg(
            destination.Id,
            TransactionType.Credit,
            request.Amount,
            request.Description,
            occurredAt);

        _dbContext.Transactions.AddRange(debitLeg, creditLeg);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // 6. The link row, which can only be built once the legs have IDs.
        var transfer = Transfer.Record(
            userId,
            source.Id,
            destination.Id,
            request.Amount,
            debitLeg,
            creditLeg);

        _dbContext.Transfers.Add(transfer);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // 7. Close the circular FK now that both rows exist (docs/04 §3.4).
        // 8. Apply the balance changes (BR-17), still under the locks from
        //    step 2. Both flushed by the single SaveChanges below.
        debitLeg.AssignTransfer(transfer.Id);
        creditLeg.AssignTransfer(transfer.Id);

        source.Apply(debitLeg);
        destination.Apply(creditLeg);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // 9. Audit inside the same transaction (BR-36). Metadata does not
        //    duplicate the financial columns (BR-37).
        await _auditService.RecordAsync(
            userId,
            AuditAction.TransferCreated,
            nameof(Transfer),
            transfer.Id,
            new { transfer.SourceAccountId, transfer.DestinationAccountId },
            cancellationToken);

        // 10. One commit. Any throw above — including a constraint violation on
        //     the credit leg — rolls back every write, so the source can never
        //     be debited for money the destination never received (BR-28).
        await databaseTransaction.CommitAsync(cancellationToken);

        return Map(transfer);
    }

    /// <summary>
    /// GET /api/transfers (contract §6). Newest first, paginated in SQL
    /// (BR-40), scoped by the denormalised UserId so ownership is a
    /// single-column predicate rather than a join through either account
    /// (BR-07).
    /// </summary>
    public async Task<PagedResponse<TransferResponse>> ListAsync(
        Guid userId,
        TransferQuery query,
        CancellationToken cancellationToken)
    {
        query.Validate();

        var owned = _dbContext.Transfers
            .AsNoTracking()
            .Where(t => t.UserId == userId);

        var totalItems = await owned.CountAsync(cancellationToken);

        // BR-41: the Id tiebreak makes the ordering total, so a row cannot
        // appear on two pages when two transfers share a timestamp.
        var items = await owned
            .OrderByDescending(t => t.OccurredAt)
            .ThenByDescending(t => t.Id)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(t => new TransferResponse(
                t.Id,
                t.SourceAccountId,
                t.DestinationAccountId,
                t.Amount,
                t.DebitTransactionId,
                t.CreditTransactionId,
                t.OccurredAt))
            .ToListAsync(cancellationToken);

        return PagedResponse<TransferResponse>.Create(
            items,
            query.Page,
            query.PageSize,
            totalItems);
    }

    /// <summary>
    /// BR-29 and BR-30 in one statement.
    ///
    /// ORDER BY "Id" is not cosmetic and is not for the caller's benefit — the
    /// result is re-matched by ID in C# anyway. It fixes the order in which
    /// PostgreSQL ACQUIRES the two row locks. Without it the engine is free to
    /// lock them in whatever order the scan produces, and two opposing
    /// transfers can each take one lock and wait forever for the other.
    ///
    /// Ownership sits in the predicate rather than in a check afterwards
    /// (BR-07), so a foreign account simply yields no row and cannot be
    /// distinguished from one that does not exist (BR-08).
    ///
    /// ToListAsync rather than a composed query, for the same reason as
    /// TransactionService.LockAccountAsync: composing over raw SQL wraps it in
    /// a subquery, and PostgreSQL rejects FOR UPDATE inside a subquery.
    ///
    /// Tracked, not AsNoTracking: step 8 mutates these instances.
    /// </summary>
    private async Task<List<Account>> LockAccountsAsync(
        Guid userId,
        Guid sourceAccountId,
        Guid destinationAccountId,
        CancellationToken cancellationToken) =>
        await _dbContext.Accounts
            .FromSql(
                $"""
                 SELECT * FROM "Accounts"
                 WHERE "Id" IN ({sourceAccountId}, {destinationAccountId})
                   AND "UserId" = {userId}
                 ORDER BY "Id"
                 FOR UPDATE
                 """)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// BR-08: identical for a foreign account and one that does not exist, and
    /// it names neither account — saying WHICH of the two was not found would
    /// confirm the other one exists.
    /// </summary>
    private static DomainException NotFound() => new(
        ErrorCodes.NotFound,
        StatusCodes.Status404NotFound,
        "The requested resource was not found.");

    private static TransferResponse Map(Transfer transfer) =>
        new(
            transfer.Id,
            transfer.SourceAccountId,
            transfer.DestinationAccountId,
            transfer.Amount,
            transfer.DebitTransactionId,
            transfer.CreditTransactionId,
            transfer.OccurredAt);
}
