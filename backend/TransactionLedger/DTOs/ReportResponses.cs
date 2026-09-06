using TransactionLedger.Domain;

namespace TransactionLedger.DTOs;

/// <summary>
/// GET /api/reports/summary (contract §7).
///
/// TotalBalance and the credit/debit figures answer DIFFERENT questions and the
/// date filter treats them differently on purpose: TotalBalance is "what do I
/// have right now" and ignores from/to, while TotalCredits, TotalDebits and
/// NetChange are "what happened in this window" and respect it. Conflating the
/// two is a classic reporting bug, so the contract documents it and the echoed
/// From/To make which window produced these numbers explicit in the response.
/// </summary>
public sealed record SummaryReportResponse(
    decimal TotalBalance,
    decimal TotalCredits,
    decimal TotalDebits,
    decimal NetChange,
    int TransactionCount,
    int AccountCount,
    DateTime? From,
    DateTime? To);

/// <summary>
/// GET /api/reports/categories (contract §7). Spending analysis, so debits
/// only — and see ReportService for the three exclusions BR-43 requires.
/// </summary>
public sealed record CategoryReportResponse(
    IReadOnlyList<CategoryReportItem> Categories,
    decimal TotalAmount);

public sealed record CategoryReportItem(
    TransactionCategory Category,
    decimal TotalAmount,
    int TransactionCount);

/// <summary>GET /api/reports/monthly (contract §7).</summary>
public sealed record MonthlyReportResponse(IReadOnlyList<MonthlyReportItem> Months);

public sealed record MonthlyReportItem(
    int Year,
    int Month,
    decimal Credits,
    decimal Debits,
    decimal Net);
