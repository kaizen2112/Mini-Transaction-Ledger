namespace TransactionLedger.Domain;

/// <summary>The audited actions listed in BR-36.</summary>
public static class AuditActions
{
    public const string UserRegistered = "UserRegistered";
    public const string AccountCreated = "AccountCreated";
    public const string TransactionCreated = "TransactionCreated";
    public const string TransactionReversed = "TransactionReversed";
    public const string TransferCreated = "TransferCreated";
}
