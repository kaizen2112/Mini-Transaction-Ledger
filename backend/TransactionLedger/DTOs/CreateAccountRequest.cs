using System.ComponentModel.DataAnnotations;
using TransactionLedger.Domain;

namespace TransactionLedger.DTOs;

/// <summary>
/// Validation per docs/05-api-contract.md §4. There is deliberately no
/// opening-balance field (BR-15) and no userId field — identity comes from
/// the token and never from the client (BR-06).
/// </summary>
public sealed record CreateAccountRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [Required]
    [EnumDataType(typeof(AccountType))]
    public AccountType Type { get; init; }
}
