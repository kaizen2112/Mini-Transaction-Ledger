using TransactionLedger.DTOs;

namespace TransactionLedger.Services;

public interface ITransactionService
{
    Task<IdempotentResult<TransactionResponse>> CreateAsync(
        Guid userId,
        Guid accountId,
        CreateTransactionRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken);

    Task<PagedResponse<TransactionResponse>> ListAsync(
        Guid userId,
        Guid accountId,
        TransactionQuery query,
        CancellationToken cancellationToken);

    Task<TransactionResponse> GetAsync(
        Guid userId,
        Guid transactionId,
        CancellationToken cancellationToken);

    Task<TransactionResponse> ReverseAsync(
        Guid userId,
        Guid transactionId,
        ReverseTransactionRequest? request,
        CancellationToken cancellationToken);
}
