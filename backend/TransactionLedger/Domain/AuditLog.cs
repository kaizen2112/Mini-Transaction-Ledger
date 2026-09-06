namespace TransactionLedger.Domain;

/// <summary>
/// "Who touched what, and when" (docs/04 §3.6).
///
/// This is NOT a second ledger (BR-37). It records the fact that an action
/// happened; the Transactions table records what the money did. Metadata is
/// deliberately small and non-financial, and a balance must never be computed
/// from it — two sources of truth for money is exactly the failure this rule
/// exists to prevent.
///
/// Append-only (BR-38), enforced by ABSENCE: there is no setter, no mutating
/// method, no Update or Delete path in any service, and no API surface at all.
/// An audit log with an edit path is not an audit log. The same technique as
/// transaction immutability (BR-21) — the missing capability IS the guarantee.
/// </summary>
public sealed class AuditLog
{
    /// <summary>docs/04 §3.6: varchar(50).</summary>
    public const int MaxEntityTypeLength = 50;

    // EF Core materialises through this; application code uses Record.
    private AuditLog()
    {
        EntityType = string.Empty;
    }

    private AuditLog(
        Guid id,
        Guid userId,
        AuditAction action,
        string entityType,
        Guid entityId,
        string? metadata,
        DateTime occurredAt)
    {
        Id = id;
        UserId = userId;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        Metadata = metadata;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }

    /// <summary>Who performed the action — always from the JWT `sub` (BR-06).</summary>
    public Guid UserId { get; private set; }

    public AuditAction Action { get; private set; }

    /// <summary>Account, Transaction, Transfer or User.</summary>
    public string EntityType { get; private set; }

    public Guid EntityId { get; private set; }

    /// <summary>
    /// Small, non-financial JSON (BR-37). Stored as jsonb so it is queryable,
    /// but held here as a string: the domain has no reason to understand the
    /// shape, and giving it one would invite the financial payload back in.
    /// </summary>
    public string? Metadata { get; private set; }

    public DateTime OccurredAt { get; private set; }

    public static AuditLog Record(
        Guid userId,
        AuditAction action,
        string entityType,
        Guid entityId,
        string? metadata) =>
        new(
            Guid.CreateVersion7(),
            userId,
            action,
            entityType,
            entityId,
            metadata,
            // BR-26: server clock, never a caller-supplied value.
            DateTime.UtcNow);
}
