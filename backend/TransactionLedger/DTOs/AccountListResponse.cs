namespace TransactionLedger.DTOs;

/// <summary>
/// Not paginated (contract §4): the realistic upper bound is a handful of
/// accounts and the dashboard needs all of them plus the total.
/// </summary>
public sealed record AccountListResponse(IReadOnlyList<AccountResponse> Accounts, decimal TotalBalance);
