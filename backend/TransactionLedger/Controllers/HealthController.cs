using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TransactionLedger.DTOs;

namespace TransactionLedger.Controllers;

/// <summary>
/// Liveness endpoint. Anonymous, and routed at the application root rather than
/// under /api, per docs/05-api-contract.md §2 and §9. The Docker healthcheck
/// calls it (docs/08-docker.md §5). 200 means the app and its database
/// connection are both up (contract §2, test K7); 503 means the database is
/// unreachable, matching the framework convention curl -f treats as a failure.
/// </summary>
[ApiController]
public sealed class HealthController : ControllerBase
{
    private readonly HealthCheckService _healthCheckService;

    public HealthController(HealthCheckService healthCheckService)
    {
        _healthCheckService = healthCheckService;
    }

    // [AllowAnonymous] is redundant today and load-bearing from B4 onwards,
    // when a global fallback authorization policy exists.
    [HttpGet("/health")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var report = await _healthCheckService.CheckHealthAsync(cancellationToken);
        var response = new HealthResponse(report.Status == HealthStatus.Healthy ? "Healthy" : "Unhealthy");

        return report.Status == HealthStatus.Healthy
            ? Ok(response)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, response);
    }
}
