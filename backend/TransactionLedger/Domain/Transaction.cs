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

    /// <summary>
    /// Creates one leg of a transfer (BR-27). Both legs are real ledger
    /// entries, not bookkeeping shortcuts, because the account's history must
    /// explain its balance: a transfer represented only by a Transfers row
    /// would show a balance change with no matching entry and break BR-17.
    ///
    /// Two differences from <see cref="Record"/>, both deliberate:
    ///
    ///   - Category is forced to Transfer. <see cref="GuardCategory"/> is not
    ///     called, and must not be: BR-24 forbids a CLIENT from supplying a
    ///     system-only category, not the system from assigning one.
    ///   - OccurredAt is supplied rather than read from the clock, so both legs
    ///     share one instant. Two separate DateTime.UtcNow reads could straddle
    ///     a tick and sort the legs apart in history (BR-41).
    /// </summary>
    public static Transaction RecordTransferLeg(
        Guid accountId,
        TransactionType type,
        decimal amount,
        string? description,
        DateTime occurredAt)
    {
        GuardAmount(amount);
        var trimmed = GuardDescription(description);

        return new Transaction(
            Guid.CreateVersion7(),
            accountId,
            type,
            amount,
            TransactionCategory.Transfer,
            trimmed,
            occurredAt);
    }

    /// <summary>
    /// Creates the compensating entry that cancels <paramref name="original"/>
    /// (BR-22): opposite type, equal amount, category Reversal, linked back to
    /// what it undoes.
    ///
    /// The link is stored HERE, on the reversal, and never as a
    /// "ReversedByTransactionId" on the original — writing that column would
    /// mean updating the original row, which BR-21 forbids. Pointing backwards
    /// keeps every row write-once, and "has this been reversed?" becomes a
    /// question the query answers (an EXISTS against UX_Transactions_Reverses)
    /// rather than a flag someone has to remember to set.
    ///
    /// The eligibility rules of BR-23 are NOT checked here. They need the
    /// database (has it already been reversed? would the balance go negative?)
    /// and belong to the service, under the row lock.
    /// </summary>
    public static Transaction Reverse(Transaction original, string? description)
    {
        var opposite = original.Type switch
        {
            TransactionType.Credit => TransactionType.Debit,
            TransactionType.Debit => TransactionType.Credit,
            _ => throw new InvalidOperationException($"Unknown transaction type {original.Type}.")
        };

        var trimmed = GuardDescription(description)
                      ?? $"Reversal of {original.Type} {original.Amount:0.00}";

        return new Transaction(
            Guid.CreateVersion7(),
            original.AccountId,
            opposite,
            // Equal amount, not a recomputed one: the reversal must net the
            // original to exactly zero (BR-22).
            original.Amount,
            TransactionCategory.Reversal,
            trimmed,
            // BR-26: the reversal happens NOW. It does not inherit the
            // original's timestamp — that would backdate a correction into
            // history and make the account's own statement misleading.
            DateTime.UtcNow)
        {
            ReversesTransactionId = original.Id
        };
    }

    /// <summary>
    /// Backfills the link to the parent transfer (docs/04 §3.4, circular FK
    /// note). Transactions.TransferId and Transfers.DebitTransactionId point at
    /// each other, so one side has to be written second.
    ///
    /// This is NOT a violation of BR-21. Immutability means a COMMITTED row is
    /// never altered; this runs inside the same uncommitted transaction that
    /// created the row, before anything is visible to any other session. The
    /// guard below makes the one-shot nature explicit.
    /// </summary>
    public void AssignTransfer(Guid transferId)
    {
        if (TransferId.HasValue)
        {
            throw new InvalidOperationException(
                "This transaction already belongs to a transfer.");
        }

        TransferId = transferId;
    }

    /// <summary>
    /// BR-03/04/05, exposed so a caller can reject a bad amount BEFORE opening
    /// a transaction and taking row locks. Validating only inside
    /// <see cref="Record"/> would still be correct — the rollback undoes
    /// everything — but it would take exclusive locks on two accounts to
    /// discover that the client sent a negative number.
    /// </summary>
    public static void ValidateAmount(decimal amount) => GuardAmount(amount);

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
