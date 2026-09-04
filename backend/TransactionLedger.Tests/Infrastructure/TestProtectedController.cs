using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransactionLedger.Extensions;

namespace TransactionLedger.Tests.Infrastructure;

/// <summary>
/// Lives in the TEST assembly only and is added to the host as an application
/// part by ApiFactory. It exists because A7-A9 need a protected endpoint and
/// the first real one does not arrive until B5. Everything it exercises is
/// real: the real host, real UseAuthentication, real token validation, real
/// GetUserId(). Only the endpoint itself is a fixture.
/// </summary>
[ApiController]
[Route("test/protected")]
[Authorize]
public sealed class TestProtectedController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { userId = User.GetUserId() });
}
