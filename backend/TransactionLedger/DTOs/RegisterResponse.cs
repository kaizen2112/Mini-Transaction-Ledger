namespace TransactionLedger.DTOs;

/// <summary>
/// Deliberately carries no token: registration and login are separate so the
/// login path has exactly one implementation (contract §3).
/// </summary>
public sealed record RegisterResponse(Guid Id, string Email, string DisplayName, DateTime CreatedAt);
