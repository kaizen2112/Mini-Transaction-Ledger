using Microsoft.AspNetCore.Http;

namespace TransactionLedger.Domain;

/// <summary>
/// A ledger entry. Immutable once committed (BR-21): no service method mutates
/// a loaded Transaction and there is no PUT or DELETE endpoint. A correction is
/// a compensating entry whose <see cref="ReversesTransactionId"/> points back at
/// the original, so fixing a mistake never touches the original row (BR-22).
/// </summary>
public sealed class Transaction
{
    /// <summary>BR-04. Also enforced by CK_Transactions_AmountMax.</summary>
    public const decimal MaxAmount = 1_000_000_000m;

    /// <summary>BR-25.</summary>
    public const int MaxDescriptionLength = 500;

    // EF Core materialises through this; application code uses Record.
    private Transaction()
    {
    }

    private Transaction(
        Guid id,
        Guid accountId,
        TransactionType type,
        decimal amount,
        TransactionCategory category,
        string? description,
        DateTime occurredAt)
    {
        Id = id;
        AccountId = accountId;
        Type = type;
        Amount = amount;
        Category = category;
        Description = description;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    public TransactionType Type { get; private set; }

    /// <summary>Always strictly positive (BR-03).</summary>
    public decimal Amount { get; private set; }

    public TransactionCategory Category { get; private set; }

    public string? Description { get; private set; }

    /// <summary>UTC, set by the server clock. Clients cannot backdate (BR-26).</summary>
    public DateTime OccurredAt { get; private set; }

    /// <summary>
    /// Set only on a reversal row (B9). The link points BACKWARDS from the
    /// reversal to the original so the original is never written to (BR-22).
    /// </summary>
    public Guid? ReversesTransactionId { get; private set; }

    /// <summary>Set only on transfer legs (B8).</summary>
    public Guid? TransferId { get; private set; }

    /// <summary>
    /// Creates a user-initiated credit or debit. Every guard here has a
    /// matching CHECK constraint in the database: the application produces the
    /// useful error message, the constraint is the backstop that turns a
    /// forgotten check into a loud failure rather than silent corruption.
    /// </summary>
    public static Transaction Record(
        Guid accountId,
        TransactionType type,
        decimal amount,
        TransactionCategory category,
        string? description)
    {
        GuardAmount(amount);
        GuardCategory(category);
        var trimmed = GuardDescription(description);

        return new Transaction(
            Guid.CreateVersion7(),
            accountId,
            type,
            amount,
            category,
            trimmed,
            // BR-26: server clock, never a client-supplied value.
            DateTime.UtcNow);
    }

    private static void GuardAmount(decimal amount)
    {
        // BR-03
        if (amount <= decimal.Zero)
        {
            throw new DomainException(
                ErrorCodes.AmountNotPositive,
                StatusCodes.Status400BadRequest,
                "Amount must be greater than zero.");
        }

        // BR-04
        if (amount > MaxAmount)
        {
            throw new DomainException(
                ErrorCodes.AmountTooLarge,
                StatusCodes.Status400BadRequest,
                $"Amount must not exceed {MaxAmount:0.00}.");
        }

        // BR-05: no rounding is performed. Silent rounding would mean the
        // number stored is not the number the user sent.
        if (decimal.Round(amount, 2) != amount)
        {
            throw new DomainException(
                ErrorCodes.AmountScaleInvalid,
                StatusCodes.Status400BadRequest,
                "Amount must have at most two decimal places.");
        }
    }

    private static void GuardCategory(TransactionCategory category)
    {
        // BR-24
        if (category.IsSystemOnly())
        {
            throw new DomainException(
                ErrorCodes.CategorySystemOnly,
                StatusCodes.Status400BadRequest,
                $"Category '{category}' is assigned by the system and cannot be supplied.");
        }
    }

    private static string? GuardDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var trimmed = description.Trim();

        // BR-25
        if (trimmed.Length > MaxDescriptionLength)
        {
            throw new DomainException(
                ErrorCodes.DescriptionTooLong,
                StatusCodes.Status400BadRequest,
                $"Description must not exceed {MaxDescriptionLength} characters.");
        }

        return trimmed;
    }
}
