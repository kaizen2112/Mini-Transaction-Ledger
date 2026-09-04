using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TransactionLedger.Configuration;
using TransactionLedger.Domain;

namespace TransactionLedger.Services;

/// <summary>
/// Issues the 60-minute access token described in docs/05-api-contract.md §3.
/// Claims: sub, email, jti, iat, exp, iss, aud. No refresh token in this
/// version. The payload carries no secret — a JWT is signed, not encrypted,
/// so anyone holding it can read every claim.
/// </summary>
public sealed class TokenService : ITokenService
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(60);

    private readonly JwtSettings _settings;

    public TokenService(IOptions<JwtSettings> settings)
    {
        _settings = settings.Value;
    }

    public (string AccessToken, DateTime ExpiresAt) CreateToken(User user)
    {
        var issuedAt = DateTime.UtcNow;
        var expiresAt = issuedAt.Add(Lifetime);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(
                JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(issuedAt).ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key));
        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            // No notBefore: contract §3 enumerates sub, email, jti, iat, exp,
            // iss, aud. Lifetime is enforced by exp alone.
            expires: expiresAt,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
