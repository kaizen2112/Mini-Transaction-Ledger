namespace TransactionLedger.DTOs;

/// <summary>
/// Query string for GET /api/transfers (contract §6): "same pagination rules as
/// transaction history", and no filters.
///
/// A separate type from TransactionQuery rather than a reuse of it. Reusing it
/// would advertise type, category, search and amount filters on an endpoint the
/// contract gives none, and a bound-but-ignored filter is worse than an absent
/// one: the caller believes it filtered. The pagination bounds themselves are
/// shared through PaginationRules, so the part that must not drift does not.
/// </summary>
public sealed record TransferQuery
{
    public int Page { get; init; } = PaginationRules.DefaultPage;

    public int PageSize { get; init; } = PaginationRules.DefaultPageSize;

    public int Skip => (Page - 1) * PageSize;

    public void Validate() => PaginationRules.Validate(Page, PageSize);
}
