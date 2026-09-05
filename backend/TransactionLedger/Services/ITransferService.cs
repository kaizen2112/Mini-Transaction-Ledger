using TransactionLedger.DTOs;

namespace TransactionLedger.Services;

public interface ITransferService
{
    Task<TransferResponse> CreateAsync(
        Guid userId,
        CreateTransferRequest request,
        CancellationToken cancellationToken);

    Task<PagedResponse<TransferResponse>> ListAsync(
        Guid userId,
        TransferQuery query,
        CancellationToken cancellationToken);
}
