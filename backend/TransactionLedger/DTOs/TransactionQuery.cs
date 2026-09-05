using Microsoft.AspNetCore.Http;
using TransactionLedger.Domain;

namespace TransactionLedger.DTOs;

/// <summary>
/// Query string for transaction history, per docs/05-api-contract.md §5 and
/// BR-42.
///
/// No DataAnnotations here on purpose: every bound value collapses to
/// VALIDATION_FAILED, but the contract requires PAGINATION_INVALID and
/// FILTER_RANGE_INVALID specifically. The guards live in Validate() so each
/// failure carries its own code (same reasoning as CreateTransactionRequest).
/// </summary>
public sealed record TransactionQuery
{
    // Shared with TransferQuery via PaginationRules, so the two histories
    // cannot drift to different page-size ceilings (BR-39).
    public const int DefaultPage = PaginationRules.DefaultPage;
    public const int DefaultPageSize = PaginationRules.DefaultPageSize;
    public const int MaxPageSize = PaginationRules.MaxPageSize;
    public const int MaxSearchLength = 100;

    public int Page { get; init; } = DefaultPage;

    public int PageSize { get; init; } = DefaultPageSize;

    public TransactionType? Type { get; init; }

    public TransactionCategory? Category { get; init; }

    public DateTime? From { get; init; }

    public DateTime? To { get; init; }

    public decimal? MinAmount { get; init; }

    public decimal? MaxAmount { get; init; }

    public string? Search { get; init; }

    public int Skip => (Page - 1) * PageSize;

    /// <summary>
    /// BR-39: out-of-range pagination is REJECTED, never silently clamped.
    /// Returning 100 rows to a client that asked for 5000 makes it believe it
    /// has everything.
    /// BR-42: inverted ranges are rejected too.
    /// </summary>
    public void Validate()
    {
        PaginationRules.Validate(Page, PageSize);

        if (From.HasValue && To.HasValue && From > To)
        {
            throw Range("from must be earlier than or equal to to.");
        }

        if (MinAmount.HasValue && MaxAmount.HasValue && MinAmount > MaxAmount)
        {
            throw Range("minAmount must be less than or equal to maxAmount.");
        }

        if (Search is { Length: > MaxSearchLength })
        {
            throw Range($"search must not exceed {MaxSearchLength} characters.");
        }
    }

    private static DomainException Range(string detail) =>
        new(ErrorCodes.FilterRangeInvalid, StatusCodes.Status400BadRequest, detail);
}
