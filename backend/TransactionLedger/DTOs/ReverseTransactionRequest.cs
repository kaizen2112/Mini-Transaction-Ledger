namespace TransactionLedger.DTOs;

/// <summary>
/// Optional body for POST /api/transactions/{id}/reverse (contract §5): the
/// request carries no amount, no type and no account — every one of those is
/// derived from the original, because a reversal that could disagree with what
/// it reverses would not net to zero (BR-22).
///
/// The whole body is optional, so the controller binds it as nullable.
/// </summary>
public sealed record ReverseTransactionRequest(string? Description);
