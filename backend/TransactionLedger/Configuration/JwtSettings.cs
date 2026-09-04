using System.ComponentModel.DataAnnotations;

namespace TransactionLedger.Configuration;

/// <summary>
/// Bound from the "Jwt" configuration section. Validated at startup with
/// ValidateOnStart so the app refuses to boot with a weak key rather than
/// failing on the first login attempt (docs/08-docker.md §6).
/// </summary>
public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    [Required]
    [MinLength(32, ErrorMessage = "Jwt:Key must be at least 32 characters for HMAC-SHA256.")]
    public string Key { get; init; } = string.Empty;

    [Required]
    public string Issuer { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;
}
