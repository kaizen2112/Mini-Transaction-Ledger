using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransactionLedger.DTOs;

namespace TransactionLedger.Controllers;

/// <summary>
/// Liveness endpoint. Anonymous, and routed at the application root rather than
/// under /api, per docs/05-api-contract.md §2 and §9. The Docker healthcheck
/// calls it (docs/08-docker.md §5).
/// </summary>
[ApiController]
public sealed class HealthController : ControllerBase
{
    // [AllowAnonymous] is redundant today and load-bearing from B4 onwards,
    // when a global fallback authorization policy exists.
    [HttpGet("/health")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public IActionResult Get() => Ok(new HealthResponse("Healthy"));
}
