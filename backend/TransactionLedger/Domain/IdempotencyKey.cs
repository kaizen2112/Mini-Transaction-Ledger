using Microsoft.AspNetCore.Http;

namespace TransactionLedger.Domain;

/// <summary>
/// One client-supplied retry token, per docs/04-database-design.md §3.5.
///
/// The row itself is almost incidental — the mechanism is the UNIQUE index on
/// (UserId, Endpoint, Key). Because the INSERT happens inside the same database
/// transaction as the financial work (BR-34), a genuinely concurrent duplicate
/// BLOCKS on that index until the first request commits, then fails, then
/// replays the committed response. There is no window where two requests both
/// proceed, no "in progress" flag, and no timeout to tune — the same
/// database-decides pattern as BR-11 and the double-reversal guard in BR-22.
/// </summary>
public sealed class IdempotencyKey
{
    /// <summary>BR-33.</summary>
    public const int MinKeyLength = 8;

    /// <summary>BR-33. Also the column width (docs/04 §3.5).</summary>
    public const int MaxKeyLength = 128;

    public const int MaxEndpointLength = 100;

    /// <summary>SHA-256 as lowercase hex, so char(64).</summary>
    public const int HashLength = 64;

    // EF Core materialises through this; application code uses Claim.
    private IdempotencyKey()
    {
        Endpoint = string.Empty;
        Key = string.Empty;
        RequestHash = string.Empty;
        ResponseBody = string.Empty;
    }

    private IdempotencyKey(
        Guid id,
        Guid userId,
        string endpoint,
        string key,
        string requestHash,
        DateTime createdAt)
    {
        Id = id;
        UserId = userId;
        Endpoint = endpoint;
        Key = key;
        RequestHash = requestHash;
        CreatedAt = createdAt;

        // Placeholders, overwritten by Complete before the transaction commits.
        // docs/04 §3.5 makes both columns NOT NULL, and this state is never
        // observable: a concurrent duplicate blocks until this row's
        // transaction commits (by which time Complete has run) or rolls back
        // (by which time the row does not exist).
        ResponseStatusCode = 0;
        ResponseBody = "null";
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>The route template, not the concrete URL — see IdempotencyService.</summary>
    public string Endpoint { get; private set; }

    public string Key { get; private set; }

    /// <summary>
    /// SHA-256 of the canonicalised request. Compared on replay so that the
    /// same key with a different body is caught as the client bug it is (BR-34),
    /// rather than silently returning the wrong stored response.
    /// </summary>
    public string RequestHash { get; private set; }

    public int ResponseStatusCode { get; private set; }

    public string ResponseBody { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public static IdempotencyKey Claim(Guid userId, string endpoint, string key, string requestHash) =>
        new(Guid.CreateVersion7(), userId, endpoint, key, requestHash, DateTime.UtcNow);

    /// <summary>
    /// Stores the response that a replay will return. Called before the commit
    /// that makes this row visible, so it is part of creating the record rather
    /// than mutating a committed one.
    /// </summary>
    public void Complete(int statusCode, string responseBody)
    {
        ResponseStatusCode = statusCode;
        ResponseBody = responseBody;
    }

    /// <summary>
    /// BR-32/BR-33. The header is REQUIRED on the two money-moving endpoints:
    /// an optional safety mechanism is one that is absent in production.
    /// </summary>
    public static string ValidateKey(string? key)
    {
        var trimmed = key?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            throw Missing("The Idempotency-Key header is required on this endpoint.");
        }

        if (trimmed.Length is < MinKeyLength or > MaxKeyLength)
        {
            throw Missing(
                $"The Idempotency-Key header must be between {MinKeyLength} and {MaxKeyLength} characters.");
        }

        return trimmed;
    }

    /// <summary>
    /// A malformed key uses the same code as an absent one: the contract
    /// defines exactly two idempotency codes (§1.2), and from the caller's
    /// point of view both mean "you did not supply a usable key".
    /// </summary>
    private static DomainException Missing(string detail) => new(
        ErrorCodes.IdempotencyKeyMissing,
        StatusCodes.Status400BadRequest,
        detail);
}
