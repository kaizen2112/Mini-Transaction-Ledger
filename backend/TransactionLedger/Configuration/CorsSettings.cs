using System.ComponentModel.DataAnnotations;

namespace TransactionLedger.Configuration;

/// <summary>
/// The browser origins allowed to call this API. Bound from the "Cors" section
/// and validated at startup like <see cref="JwtSettings"/>, so a bad origin list
/// fails the boot rather than silently breaking every request the frontend
/// makes — a CORS failure is invisible server-side, and debugging it from the
/// browser console is far more expensive than crashing here.
///
/// AllowAnyOrigin would in fact work: the token travels in an Authorization
/// header rather than a cookie, so AllowCredentials is never needed and the two
/// are never in conflict. Naming the origin costs one configuration line, is
/// what a real deployment does, and keeps the policy honest if cookie auth is
/// ever introduced.
/// </summary>
public sealed class CorsSettings
{
    public const string SectionName = "Cors";

    /// <summary>
    /// Named so AddPolicy and UseCors cannot drift apart. An unmatched policy
    /// name is not an error — the middleware just applies nothing, and every
    /// cross-origin request fails with no server-side symptom.
    /// </summary>
    public const string PolicyName = "WebApp";

    [Required]
    [MinLength(1, ErrorMessage = "Cors:AllowedOrigins must list at least one origin.")]
    public string[] AllowedOrigins { get; init; } = [];
}
