using TransactionLedger.Domain;

namespace TransactionLedger.DTOs;

/// <summary>Shared response object from docs/05-api-contract.md §1.3.</summary>
public sealed record AccountResponse(
    Guid Id,
    string Name,
    AccountType Type,
    decimal Balance,
    DateTime CreatedAt);
