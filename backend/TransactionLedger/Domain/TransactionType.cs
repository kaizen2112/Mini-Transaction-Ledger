namespace TransactionLedger.Domain;

/// <summary>
/// Direction of a transaction. BR-03: direction lives here and never in the
/// sign of the amount — a "credit of -50" would otherwise be a debit that
/// bypasses every debit rule.
///
/// Values are explicit because they persist as integers
/// (docs/04-database-design.md §1.4/§3.3).
/// </summary>
public enum TransactionType
{
    Credit = 0,
    Debit = 1
}
