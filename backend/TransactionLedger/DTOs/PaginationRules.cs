using Microsoft.AspNetCore.Http;
using TransactionLedger.Domain;

namespace TransactionLedger.DTOs;

/// <summary>
/// BR-39, in one place. Transaction history and transfer history are separate
/// query types with different filters, but their pagination contract is
/// identical — so the bounds and the guard live here rather than being copied,
/// where the two could silently drift to different page-size ceilings.
/// </summary>
public static class PaginationRules
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>
    /// BR-39: out-of-range pagination is REJECTED, never silently clamped.
    /// Returning 100 rows to a client that asked for 5000 makes it believe it
    /// has everything, and it stops paging.
    /// </summary>
    public static void Validate(int page, int pageSize)
    {
        if (page < 1)
        {
            throw Invalid($"page must be 1 or greater; received {page}.");
        }

        if (pageSize < 1 || pageSize > MaxPageSize)
        {
            throw Invalid($"pageSize must be between 1 and {MaxPageSize}; received {pageSize}.");
        }
    }

    private static DomainException Invalid(string detail) =>
        new(ErrorCodes.PaginationInvalid, StatusCodes.Status400BadRequest, detail);
}
