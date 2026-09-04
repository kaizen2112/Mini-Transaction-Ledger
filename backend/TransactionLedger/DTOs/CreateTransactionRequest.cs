using System.ComponentModel.DataAnnotations;
using TransactionLedger.Domain;

namespace TransactionLedger.DTOs;

/// <summary>
/// Request body per docs/05-api-contract.md §5.
///
/// Note what is NOT here: no accountId (it is the route), no userId (BR-06),
/// and no occurredAt — the server clock sets it and a client-supplied value is
/// ignored (BR-26).
///
/// Amount carries no [Range] attribute on purpose. DataAnnotations failures all
/// collapse to VALIDATION_FAILED, but the contract requires the specific codes
/// AMOUNT_NOT_POSITIVE / AMOUNT_TOO_LARGE / AMOUNT_SCALE_INVALID, so those
/// guards live in the domain factory instead (BR-03/04/05).
/// </summary>
public sealed record CreateTransactionRequest
{
    [Required]
    [EnumDataType(typeof(TransactionType))]
    public TransactionType Type { get; init; }

    [Required]
    public decimal Amount { get; init; }

    [Required]
    [EnumDataType(typeof(TransactionCategory))]
    public TransactionCategory Category { get; init; }

    public string? Description { get; init; }
}
