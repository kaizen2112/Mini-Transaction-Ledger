namespace TransactionLedger.Domain;

/// <summary>
/// Fixed enum per BR-24. Category reports (FR-18) group by this field, and
/// free text would produce near-duplicate groups.
///
/// Transfer and Reversal are SYSTEM-ONLY: the system assigns them to transfer
/// legs and reversal rows, and they are rejected when supplied by a client
/// (400, CATEGORY_SYSTEM_ONLY). See <see cref="IsSystemOnly"/>.
/// </summary>
public enum TransactionCategory
{
    Salary = 0,
    Food = 1,
    Transport = 2,
    Bills = 3,
    Shopping = 4,
    Entertainment = 5,
    Health = 6,
    Other = 7,

    /// <summary>System-only (BR-24). Assigned to transfer legs in B8.</summary>
    Transfer = 8,

    /// <summary>System-only (BR-24). Assigned to reversal rows in B9.</summary>
    Reversal = 9,

    /// <summary>
    /// Explicit value, not 8 (Transfer's slot): the column is a plain
    /// integer with no CHECK constraint, so any already-stored row's
    /// category is only as correct as its number never being reassigned.
    /// </summary>
    Rent = 10
}

public static class TransactionCategoryExtensions
{
    public static bool IsSystemOnly(this TransactionCategory category) =>
        category is TransactionCategory.Transfer or TransactionCategory.Reversal;
}
