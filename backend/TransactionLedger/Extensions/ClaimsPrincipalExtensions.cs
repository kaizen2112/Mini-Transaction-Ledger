using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace TransactionLedger.Extensions;

/// <summary>
/// BR-06: the ONLY way any code learns the caller's identity. No endpoint
/// accepts a userId from the route, query string, or body — if the client can
/// name the owner, the client can name someone else's (IDOR).
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);

        return Guid.TryParse(subject, out var userId)
            ? userId
            : throw new InvalidOperationException("The token carries no usable sub claim.");
    }
}
