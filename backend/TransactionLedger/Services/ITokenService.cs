using TransactionLedger.Domain;

namespace TransactionLedger.Services;

public interface ITokenService
{
    (string AccessToken, DateTime ExpiresAt) CreateToken(User user);
}
