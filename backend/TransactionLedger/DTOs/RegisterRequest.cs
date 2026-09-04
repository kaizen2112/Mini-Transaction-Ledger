using System.ComponentModel.DataAnnotations;

namespace TransactionLedger.DTOs;

/// <summary>Validation per docs/05-api-contract.md §3.</summary>
public sealed record RegisterRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 8)]
    public string Password { get; init; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string DisplayName { get; init; } = string.Empty;
}
