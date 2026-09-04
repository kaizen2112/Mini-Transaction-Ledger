using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TransactionLedger.Controllers;
using TransactionLedger.DTOs;

namespace TransactionLedger.Tests.Unit;

public sealed class HealthControllerTests
{
    [Fact]
    public async Task Get_returns_200_when_the_database_check_is_healthy()
    {
        var controller = BuildController(HealthCheckResult.Healthy());

        var result = await controller.Get(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.Equal(new HealthResponse("Healthy"), ok.Value);
    }

    [Fact]
    public async Task Get_returns_503_when_the_database_check_is_unhealthy()
    {
        var controller = BuildController(HealthCheckResult.Unhealthy());

        var result = await controller.Get(CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
        Assert.Equal(new HealthResponse("Unhealthy"), objectResult.Value);
    }

    private static HealthController BuildController(HealthCheckResult fakeResult)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks().AddCheck("fake", () => fakeResult);
        var provider = services.BuildServiceProvider();
        return new HealthController(provider.GetRequiredService<HealthCheckService>());
    }
}
