using TransactionLedger.DTOs;

namespace TransactionLedger.Services;

public interface ITransferService
{
    Task<IdempotentResult<TransferResponse>> CreateAsync(
        Guid userId,
        CreateTransferRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken);

    Task<PagedResponse<TransferResponse>> ListAsync(
        Guid userId,
        TransferQuery query,
        CancellationToken cancellationToken);
}
