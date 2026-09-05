using TransactionLedger.DTOs;

namespace TransactionLedger.Services;

public interface ITransactionService
{
    Task<TransactionResponse> CreateAsync(
        Guid userId,
        Guid accountId,
        CreateTransactionRequest request,
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
}
