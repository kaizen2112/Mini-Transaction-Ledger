using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace TransactionLedger.Tests.Infrastructure;

/// <summary>
/// Mints tokens the API did not issue, so A8 and A9 can prove the handler
/// actually validates lifetime and signing key rather than merely decoding.
/// </summary>
public static class JwtTestTokens
{
    public static string ReadClaim(string token, string claimType) =>
        new JwtSecurityTokenHandler().ReadJwtToken(token).Claims
            .First(claim => claim.Type == claimType)
            .Value;

    public static string Create(
        Guid userId,
        string key = ApiFactory.JwtKey,
        string issuer = ApiFactory.JwtIssuer,
        string audience = ApiFactory.JwtAudience,
        TimeSpan? lifetime = null)
    {
        var issuedAt = DateTime.UtcNow;
        var expires = issuedAt.Add(lifetime ?? TimeSpan.FromMinutes(60));

        // For an already-expired token, nbf must still be strictly before exp
        // or the library refuses to construct it at all.
        var notBefore = expires > issuedAt ? issuedAt : expires.AddMinutes(-1);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: new[] { new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()) },
            notBefore: notBefore,
            expires: expires,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
