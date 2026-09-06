using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TransactionLedger.Data;
using TransactionLedger.Domain;
using TransactionLedger.DTOs;

namespace TransactionLedger.Services;

public sealed class AccountService : IAccountService
{
    private const string UniqueViolationSqlState = "23505";
    private const string NameUniqueIndexName = "UX_Accounts_User_NameLower";

    private readonly AppDbContext _dbContext;
    private readonly IAuditService _auditService;

    public AccountService(AppDbContext dbContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _auditService = auditService;
    }

    public async Task<AccountResponse> CreateAsync(
        Guid userId,
        CreateAccountRequest request,
        CancellationToken cancellationToken)
    {
        var account = Account.Open(userId, request.Name, request.Type);

        // Two rows, one commit (BR-36) — see the same note in
        // AuthService.RegisterAsync.
        await using var databaseTransaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        _dbContext.Accounts.Add(account);

        try
        {
            // BR-11: the functional unique index on (UserId, LOWER(Name)) is
            // the only thing that actually guarantees uniqueness. A
            // SELECT-then-INSERT pre-check is a TOCTOU race — two concurrent
            // requests both see the name free and both insert.
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsNameUniqueViolation(ex))
        {
            throw new DomainException(
                ErrorCodes.AccountNameTaken,
                StatusCodes.Status409Conflict,
                "You already have an account with that name.");
        }

        // BR-36. Type but not balance: a new account is always 0.00 (BR-15),
        // and recording a balance here would be the start of the second ledger
        // BR-37 forbids.
        await _auditService.RecordAsync(
            userId,
            AuditAction.AccountCreated,
            nameof(Account),
            account.Id,
            new { Type = account.Type.ToString() },
            cancellationToken);

        await databaseTransaction.CommitAsync(cancellationToken);

        return Map(account);
    }

    public async Task<AccountListResponse> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var owned = _dbContext.Accounts
            .AsNoTracking()
            .Where(a => a.UserId == userId);

        var accounts = await owned
            .OrderBy(a => a.CreatedAt)
            .Select(a => new AccountResponse(a.Id, a.Name, a.Type, a.Balance, a.CreatedAt))
            .ToListAsync(cancellationToken);

        // Aggregated in SQL, not by summing the list in memory (BR-40,
        // contract §4). It is a second round trip, but the rule is that
        // aggregation happens in the database.
        var totalBalance = await owned.SumAsync(a => a.Balance, cancellationToken);

        return new AccountListResponse(accounts, totalBalance);
    }

    public async Task<AccountResponse> GetAsync(
        Guid userId,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var account = await OwnedBy(userId)
            .Where(a => a.Id == accountId)
            .Select(a => new AccountResponse(a.Id, a.Name, a.Type, a.Balance, a.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);

        return account ?? throw NotFound();
    }

    public async Task<AccountBalanceResponse> GetBalanceAsync(
        Guid userId,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        // Projects only the balance: a balance poll should not drag the whole
        // row across the wire (contract §4).
        var balance = await OwnedBy(userId)
            .Where(a => a.Id == accountId)
            .Select(a => (decimal?)a.Balance)
            .SingleOrDefaultAsync(cancellationToken);

        return balance is null
            ? throw NotFound()
            : new AccountBalanceResponse(accountId, balance.Value, DateTime.UtcNow);
    }

    /// <summary>
    /// BR-07: ownership is part of the query predicate, never a post-load
    /// check. That way "forgot the check" and "forgot the filter" are the same
    /// mistake, and the query simply returns nothing.
    /// </summary>
    private IQueryable<Account> OwnedBy(Guid userId) =>
        _dbContext.Accounts.AsNoTracking().Where(a => a.UserId == userId);

    /// <summary>
    /// BR-08: a resource owned by someone else is reported exactly as one that
    /// does not exist. The detail deliberately names no identifier — if it
    /// echoed the requested ID, the two responses would differ and the API
    /// would leak which IDs are real.
    /// </summary>
    private static DomainException NotFound() => new(
        ErrorCodes.NotFound,
        StatusCodes.Status404NotFound,
        "The requested resource was not found.");

    private static AccountResponse Map(Account account) =>
        new(account.Id, account.Name, account.Type, account.Balance, account.CreatedAt);

    private static bool IsNameUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException postgres
        && postgres.SqlState == UniqueViolationSqlState
        && postgres.ConstraintName == NameUniqueIndexName;
}
