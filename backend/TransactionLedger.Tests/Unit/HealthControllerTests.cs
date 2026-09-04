using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TransactionLedger.Controllers;
using TransactionLedger.DTOs;

namespace TransactionLedger.Tests.Unit;

public sealed class HealthControllerTests
{
    [Fact]
    public void Get_returns_200_with_the_contracted_status()
    {
        var result = new HealthController().Get();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.Equal(new HealthResponse("Healthy"), ok.Value);
    }
}
