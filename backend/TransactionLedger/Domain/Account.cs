namespace TransactionLedger.Domain;

/// <summary>
/// An account belongs to exactly one user and is never shared (BR-13).
/// </summary>
public sealed class Account
{
    // EF Core materialises through this; application code uses Open.
    private Account()
    {
        Name = string.Empty;
    }

    private Account(Guid id, Guid userId, string name, AccountType type, DateTime createdAt)
    {
        Id = id;
        UserId = userId;
        Name = name;
        Type = type;
        CreatedAt = createdAt;
        Balance = decimal.Zero;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Name { get; private set; }

    public AccountType Type { get; private set; }

    /// <summary>
    /// Derived from the ledger and maintained transactionally (BR-17). Kept
    /// private-set so nothing outside the domain can assign a balance.
    /// </summary>
    public decimal Balance { get; private set; }

    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// BR-15: a new account starts at exactly 0.00 and there is deliberately
    /// no opening-balance parameter. An opening balance would create money
    /// outside the ledger, so the stored balance would stop equalling the sum
    /// of its transactions and break the reconciliation invariant (BR-17). A
    /// user wanting an opening balance records a credit, which is auditable
    /// and reversible.
    /// </summary>
    public static Account Open(Guid userId, string name, AccountType type) =>
        new(Guid.CreateVersion7(), userId, name.Trim(), type, DateTime.UtcNow);
}
