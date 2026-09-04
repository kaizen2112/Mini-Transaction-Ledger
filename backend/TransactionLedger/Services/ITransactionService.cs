using TransactionLedger.DTOs;

namespace TransactionLedger.Services;

public interface ITransactionService
{
    Task<TransactionResponse> CreateAsync(
        Guid userId,
        Guid accountId,
        CreateTransactionRequest request,
        CancellationToken cancellationToken);
}
