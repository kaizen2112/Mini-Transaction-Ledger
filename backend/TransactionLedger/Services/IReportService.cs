using TransactionLedger.DTOs;

namespace TransactionLedger.Services;

public interface IReportService
{
    Task<SummaryReportResponse> GetSummaryAsync(
        Guid userId,
        SummaryReportQuery query,
        CancellationToken cancellationToken);

    Task<CategoryReportResponse> GetCategoriesAsync(
        Guid userId,
        CategoryReportQuery query,
        CancellationToken cancellationToken);

    Task<MonthlyReportResponse> GetMonthlyAsync(
        Guid userId,
        MonthlyReportQuery query,
        CancellationToken cancellationToken);
}
