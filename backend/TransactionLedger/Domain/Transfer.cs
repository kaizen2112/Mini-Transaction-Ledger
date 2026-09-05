using Microsoft.AspNetCore.Http;

namespace TransactionLedger.Domain;

/// <summary>
/// The link row that turns two independent ledger entries into one movement
/// (docs/04-database-design.md §3.4).
///
/// A transfer is NOT a balance change of its own. The money actually moves
/// because of the two <see cref="Transaction"/> rows it points at: a Debit on
/// the source and a Credit on the destination. This row exists so the pair is
/// recognisable as a pair — without it an account statement would show a debit
/// with no explanation of where the money went (BR-27).
///
/// Immutable once written, for the same reason transactions are (BR-21): a
/// transfer that could be edited would let history disagree with the balances
/// it produced.
/// </summary>
public sealed class Transfer
{
    // EF Core materialises through this; application code uses Record.
    private Transfer()
    {
    }

    private Transfer(
        Guid id,
        Guid userId,
        Guid sourceAccountId,
        Guid destinationAccountId,
        decimal amount,
        Guid debitTransactionId,
        Guid creditTransactionId,
        DateTime occurredAt)
    {
        Id = id;
        UserId = userId;
        SourceAccountId = sourceAccountId;
        DestinationAccountId = destinationAccountId;
        Amount = amount;
        DebitTransactionId = debitTransactionId;
        CreditTransactionId = creditTransactionId;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// Denormalised owner (docs/04 §3.4). Derivable through either account, but
    /// storing it makes the ownership filter for GET /api/transfers a
    /// single-column predicate instead of a two-way join (BR-07). Safe because
    /// <see cref="Record"/> is only reached after both accounts have been
    /// confirmed to belong to this user.
    /// </summary>
    public Guid UserId { get; private set; }

    public Guid SourceAccountId { get; private set; }

    public Guid DestinationAccountId { get; private set; }

    public decimal Amount { get; private set; }

    public Guid DebitTransactionId { get; private set; }

    public Guid CreditTransactionId { get; private set; }

    public DateTime OccurredAt { get; private set; }

    /// <summary>
    /// Creates the link row for a pair of legs that have already been built.
    ///
    /// The amount is not re-validated here: it was validated when the two legs
    /// were created, and both legs carry the same figure. Re-checking would
    /// imply the legs and the transfer could disagree, which the caller's
    /// single code path makes impossible.
    /// </summary>
    public static Transfer Record(
        Guid userId,
        Guid sourceAccountId,
        Guid destinationAccountId,
        decimal amount,
        Transaction debitLeg,
        Transaction creditLeg)
    {
        // BR-27. Also enforced by CK_Transfers_DifferentAccounts; the service
        // rejects this before acquiring any lock, so reaching it here means a
        // caller bypassed that check.
        if (sourceAccountId == destinationAccountId)
        {
            throw new DomainException(
                ErrorCodes.SameAccountTransfer,
                StatusCodes.Status400BadRequest,
                "Source and destination accounts must be different.");
        }

        return new Transfer(
            Guid.CreateVersion7(),
            userId,
            sourceAccountId,
            destinationAccountId,
            amount,
            debitLeg.Id,
            creditLeg.Id,
            // Both legs share one timestamp: a transfer happens at one instant,
            // and two clock reads could straddle a millisecond boundary and
            // order the legs apart in history (BR-41).
            debitLeg.OccurredAt);
    }
}
