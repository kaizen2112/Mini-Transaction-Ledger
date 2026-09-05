namespace TransactionLedger.DTOs;

/// <summary>
/// Shared envelope from docs/05-api-contract.md §1.3.
///
/// TotalPages is derived rather than stored so it can never disagree with
/// TotalItems and PageSize. A page past the last one is a valid state, not an
/// error: it returns an empty Items array with correct metadata (contract §5).
/// </summary>
public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages)
{
    public static PagedResponse<T> Create(IReadOnlyList<T> items, int page, int pageSize, int totalItems) =>
        new(items, page, pageSize, totalItems, (int)Math.Ceiling(totalItems / (double)pageSize));
}
