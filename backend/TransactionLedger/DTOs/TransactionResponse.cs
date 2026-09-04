using TransactionLedger.Domain;

namespace TransactionLedger.DTOs;

/// <summary>
/// Shared response object from docs/05-api-contract.md §1.3.
///
/// IsReversed is computed by the query (a left join on the unique reversal
/// index), never stored, because the original row is never mutated (BR-21).
/// On a freshly created transaction it is always false.
/// </summary>
public sealed record TransactionResponse(
    Guid Id,
    Guid AccountId,
    TransactionType Type,
    decimal Amount,
    TransactionCategory Category,
    string? Description,
    DateTime OccurredAt,
    Guid? ReversesTransactionId,
    bool IsReversed,
    Guid? TransferId);
