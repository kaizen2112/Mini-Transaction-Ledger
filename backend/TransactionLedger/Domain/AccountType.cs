namespace TransactionLedger.Domain;

/// <summary>
/// Fixed enum rather than free text (BR-13): reports group by type, and free
/// text produces "savings", "Savings" and "Saving " as three groups. Free text
/// is allowed in the account NAME, where no grouping happens.
///
/// Values are explicit because they persist as integers
/// (docs/04-database-design.md §1.4/§3.2) — renumbering would silently
/// reinterpret every stored row.
/// </summary>
public enum AccountType
{
    Cash = 0,
    Savings = 1,
    Business = 2,
    Wallet = 3,
    Other = 4
}
