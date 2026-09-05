namespace TransactionLedger.DTOs;

/// <summary>
/// Exactly the shape in docs/05-api-contract.md §6.
///
/// It carries both leg IDs rather than the legs themselves: a caller that wants
/// the ledger entries fetches them from GET /api/transactions/{id}, which
/// already exists (B7). Embedding them would duplicate a representation that
/// has its own endpoint and its own future changes.
/// </summary>
public sealed record TransferResponse(
    Guid Id,
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount,
    Guid DebitTransactionId,
    Guid CreditTransactionId,
    DateTime OccurredAt);
