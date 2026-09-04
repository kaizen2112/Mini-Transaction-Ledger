namespace TransactionLedger.DTOs;

/// <summary>
/// Kept separate from AccountResponse because a balance-only poll should not
/// transfer the whole account object (contract §4).
/// </summary>
public sealed record AccountBalanceResponse(Guid AccountId, decimal Balance, DateTime AsOf);
