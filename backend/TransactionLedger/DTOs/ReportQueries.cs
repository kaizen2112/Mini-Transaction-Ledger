using Microsoft.AspNetCore.Http;
using TransactionLedger.Domain;

namespace TransactionLedger.DTOs;

/// <summary>
/// from/to for the summary report (contract §7). Same inclusive UTC bounds and
/// the same FILTER_RANGE_INVALID code as transaction history, so a client that
/// learned the rule on one endpoint already knows it here.
/// </summary>
public sealed record SummaryReportQuery
{
    public DateTime? From { get; init; }

    public DateTime? To { get; init; }

    public void Validate() => ReportRange.Validate(From, To);
}

/// <summary>from/to plus an optional single-account narrowing (contract §7).</summary>
public sealed record CategoryReportQuery
{
    public DateTime? From { get; init; }

    public DateTime? To { get; init; }

    /// <summary>
    /// Optional. When supplied it is still subject to BR-07/BR-08: an account
    /// belonging to someone else is a 404, not an empty report, because an
    /// empty report would confirm the account exists.
    /// </summary>
    public Guid? AccountId { get; init; }

    public void Validate() => ReportRange.Validate(From, To);
}

/// <summary>
/// Optional year for the monthly report (contract §7). Omitted means the
/// trailing 12 months, which is what a dashboard wants by default.
/// </summary>
public sealed record MonthlyReportQuery
{
    /// <summary>
    /// Bounds are deliberately generous rather than "the current year": a
    /// ledger may hold backdated imports. They exist only to reject obvious
    /// nonsense like year=0 before it reaches the database.
    /// </summary>
    public const int MinYear = 1900;

    public const int MaxYear = 9999;

    public int? Year { get; init; }

    public void Validate()
    {
        if (Year is { } year && year is < MinYear or > MaxYear)
        {
            throw new DomainException(
                ErrorCodes.FilterRangeInvalid,
                StatusCodes.Status400BadRequest,
                $"year must be between {MinYear} and {MaxYear}; received {year}.");
        }
    }
}

/// <summary>The one range rule the report queries share (BR-42).</summary>
internal static class ReportRange
{
    public static void Validate(DateTime? from, DateTime? to)
    {
        if (from.HasValue && to.HasValue && from > to)
        {
            throw new DomainException(
                ErrorCodes.FilterRangeInvalid,
                StatusCodes.Status400BadRequest,
                "from must be earlier than or equal to to.");
        }
    }
}
