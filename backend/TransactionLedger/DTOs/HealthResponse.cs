namespace TransactionLedger.DTOs;

/// <summary>
/// Body of <c>GET /health</c>, per docs/05-api-contract.md §2.
/// </summary>
public sealed record HealthResponse(string Status);
