using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransactionLedger.DTOs;
using TransactionLedger.Extensions;
using TransactionLedger.Services;

namespace TransactionLedger.Controllers;

/// <summary>
/// GET /api/reports/{summary,categories,monthly} per docs/05-api-contract.md §7.
///
/// Read-only and aggregate-only: these endpoints answer "how am I doing"
/// questions that the transaction-level endpoints cannot, without exposing any
/// new way to change data. Every one of them aggregates only over accounts
/// owned by the caller (BR-43).
///
/// The CSV export of contract §8 is deliberately NOT implemented — see the
/// scope note in docs/09-explanations.md.
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize]
public sealed class ReportsController : ControllerBase
{
    private readonly IReportService _reportService;

    public ReportsController(IReportService reportService)
    {
        _reportService = reportService;
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(SummaryReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Summary(
        [FromQuery] SummaryReportQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _reportService.GetSummaryAsync(User.GetUserId(), query, cancellationToken));

    [HttpGet("categories")]
    [ProducesResponseType(typeof(CategoryReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Categories(
        [FromQuery] CategoryReportQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _reportService.GetCategoriesAsync(User.GetUserId(), query, cancellationToken));

    [HttpGet("monthly")]
    [ProducesResponseType(typeof(MonthlyReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Monthly(
        [FromQuery] MonthlyReportQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _reportService.GetMonthlyAsync(User.GetUserId(), query, cancellationToken));
}
