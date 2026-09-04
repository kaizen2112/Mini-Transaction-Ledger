namespace TransactionLedger.DTOs;

public sealed record LoginResponse(string AccessToken, DateTime ExpiresAt, UserSummary User);

public sealed record UserSummary(Guid Id, string Email, string DisplayName);
