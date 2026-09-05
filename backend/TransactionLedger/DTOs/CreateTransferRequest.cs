namespace TransactionLedger.DTOs;

/// <summary>
/// POST /api/transfers body, per docs/05-api-contract.md §6.
///
/// No DataAnnotations, for the same reason as CreateTransactionRequest: every
/// annotation failure collapses to VALIDATION_FAILED, but the contract requires
/// AMOUNT_NOT_POSITIVE, AMOUNT_TOO_LARGE, AMOUNT_SCALE_INVALID and
/// SAME_ACCOUNT_TRANSFER as distinct codes the frontend can switch on (BR-45).
/// The guards live in the domain and the service.
///
/// There is deliberately no userId: the owner comes from the JWT `sub` claim
/// and no endpoint ever accepts an identity from the client (BR-06).
/// </summary>
public sealed record CreateTransferRequest(
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount,
    string? Description);
