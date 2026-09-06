namespace TransactionLedger.Domain;

/// <summary>
/// The five audited actions of BR-36, stored as integers (docs/04 §3.6).
///
/// An enum rather than the free-text action names this replaced: the audit log
/// is queried by action ("every reversal last month"), and free text produces
/// near-duplicate groups the moment someone writes "TransactionReverse" once.
/// Same reasoning as TransactionCategory (BR-24).
///
/// Login attempts are deliberately NOT audited in this version (BR-36).
/// </summary>
public enum AuditAction
{
    UserRegistered = 0,
    AccountCreated = 1,
    TransactionCreated = 2,
    TransactionReversed = 3,
    TransferCreated = 4
}
