namespace TransactionLedger.Domain;

/// <summary>
/// The two header names from docs/05-api-contract.md §5/§6, as constants so the
/// controllers and the tests cannot disagree about their spelling.
/// </summary>
public static class IdempotencyHeader
{
    /// <summary>Required on the two money-moving POSTs (BR-32).</summary>
    public const string Name = "Idempotency-Key";

    /// <summary>Set to "true" on a replayed response (BR-34).</summary>
    public const string ReplayName = "Idempotent-Replay";
}
