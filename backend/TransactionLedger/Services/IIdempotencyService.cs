using Microsoft.EntityFrameworkCore;
using TransactionLedger.Domain;

namespace TransactionLedger.Services;

/// <summary>
/// BR-34, split into the three moments the algorithm actually has.
///
/// It is deliberately NOT a middleware or an action filter. The key INSERT has
/// to sit inside the caller's open database transaction — that placement is the
/// entire mechanism — and a filter runs outside any transaction the service
/// later opens. Wiring it as a filter would produce something that looks
/// idempotent and races.
/// </summary>
public interface IIdempotencyService
{
    /// <summary>
    /// Step 1-2 of BR-34: hash the request and INSERT the key row. Throws a
    /// <see cref="DbUpdateException"/> the caller recognises with
    /// <see cref="IsKeyConflict"/> when the key has been used before.
    /// </summary>
    Task<IdempotencyKey> ClaimAsync(
        Guid userId,
        string endpoint,
        string key,
        object request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Step 3: store the response on the claimed row, before the caller
    /// commits, so a later replay has something to return.
    /// </summary>
    Task CompleteAsync(
        IdempotencyKey claim,
        int statusCode,
        object response,
        CancellationToken cancellationToken);

    /// <summary>
    /// Step 4: the caller's transaction has rolled back. Re-read the committed
    /// row and either return the stored response or reject the mismatched body.
    /// </summary>
    Task<TResponse> ReplayAsync<TResponse>(
        Guid userId,
        string endpoint,
        string key,
        object request,
        CancellationToken cancellationToken);

    bool IsKeyConflict(DbUpdateException exception);
}
