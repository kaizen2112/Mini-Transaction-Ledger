using TransactionLedger.DTOs;

namespace TransactionLedger.Services;

public interface IAccountService
{
    Task<AccountResponse> CreateAsync(Guid userId, CreateAccountRequest request, CancellationToken cancellationToken);

    Task<AccountListResponse> ListAsync(Guid userId, CancellationToken cancellationToken);

    Task<AccountResponse> GetAsync(Guid userId, Guid accountId, CancellationToken cancellationToken);

    Task<AccountBalanceResponse> GetBalanceAsync(Guid userId, Guid accountId, CancellationToken cancellationToken);
}
