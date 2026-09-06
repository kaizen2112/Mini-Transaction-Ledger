using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TransactionLedger.Configuration;
using TransactionLedger.Data;
using TransactionLedger.Domain;

namespace TransactionLedger.Services;

public sealed class IdempotencyService : IIdempotencyService
{
    /// <summary>The route TEMPLATE, not the concrete URL — see Endpoints below.</summary>
    public const string TransactionsEndpoint = "POST /api/accounts/{accountId}/transactions";

    public const string TransfersEndpoint = "POST /api/transfers";

    private const string UniqueViolationSqlState = "23505";
    private const string KeyUniqueIndexName = "UX_IdempotencyKeys_User_Endpoint_Key";

    /// <summary>
    /// Canonicalisation (BR-34). Hashing the RAW body would make
    /// {"amount":100} and { "amount": 100 } different requests and produce a
    /// spurious 422 on a legitimate retry from a client that reformatted its
    /// JSON. Serialising the BOUND DTO instead makes the hash a function of the
    /// request's meaning: whitespace, property order and casing all normalise
    /// away, because this is our object graph rather than their bytes.
    ///
    /// The same converters as the API, so the stored ResponseBody is byte-for-
    /// byte what the endpoint returned (BR-34, and test I3).
    /// </summary>
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(), new MoneyJsonConverter() }
    };

    private readonly AppDbContext _dbContext;

    public IdempotencyService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IdempotencyKey> ClaimAsync(
        Guid userId,
        string endpoint,
        string key,
        object request,
        CancellationToken cancellationToken)
    {
        var claim = IdempotencyKey.Claim(userId, endpoint, key, Hash(request));

        _dbContext.IdempotencyKeys.Add(claim);

        // Flushed immediately, not left to the caller's next SaveChanges. The
        // INSERT is what acquires the exclusive claim on this key, so it must
        // reach PostgreSQL before any financial work begins — that ordering is
        // what makes a concurrent duplicate block here rather than proceed.
        await _dbContext.SaveChangesAsync(cancellationToken);

        return claim;
    }

    public async Task CompleteAsync(
        IdempotencyKey claim,
        int statusCode,
        object response,
        CancellationToken cancellationToken)
    {
        claim.Complete(statusCode, JsonSerializer.Serialize(response, CanonicalOptions));

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<TResponse> ReplayAsync<TResponse>(
        Guid userId,
        string endpoint,
        string key,
        object request,
        CancellationToken cancellationToken)
    {
        // The caller's transaction has rolled back and its change tracker still
        // holds the rejected INSERT. Clearing it prevents EF from replaying
        // that doomed entity on the read below.
        _dbContext.ChangeTracker.Clear();

        var stored = await _dbContext.IdempotencyKeys
            .AsNoTracking()
            .SingleOrDefaultAsync(
                k => k.UserId == userId && k.Endpoint == endpoint && k.Key == key,
                cancellationToken);

        if (stored is null)
        {
            // The winner rolled back after we collided with it — its financial
            // work failed, so the key was never really taken (BR-34, test I6).
            // Surfacing a 409 lets the client retry, which is the truthful
            // outcome: nothing happened, try again.
            throw new DomainException(
                ErrorCodes.IdempotencyKeyReused,
                StatusCodes.Status409Conflict,
                "A concurrent request using this key did not complete. Retry.");
        }

        // BR-34: same key, different body means the CLIENT has a bug. Returning
        // the old response would hide it; performing the new operation would
        // defeat the key entirely.
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(stored.RequestHash),
                Encoding.UTF8.GetBytes(Hash(request))))
        {
            throw new DomainException(
                ErrorCodes.IdempotencyKeyReused,
                StatusCodes.Status422UnprocessableEntity,
                "This Idempotency-Key was already used with a different request body.");
        }

        return JsonSerializer.Deserialize<TResponse>(stored.ResponseBody, CanonicalOptions)
               ?? throw new InvalidOperationException(
                   "Stored idempotent response could not be deserialised.");
    }

    public bool IsKeyConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: UniqueViolationSqlState } postgres
        && postgres.ConstraintName == KeyUniqueIndexName;

    private static string Hash(object request)
    {
        var canonical = JsonSerializer.Serialize(request, CanonicalOptions);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }
}
