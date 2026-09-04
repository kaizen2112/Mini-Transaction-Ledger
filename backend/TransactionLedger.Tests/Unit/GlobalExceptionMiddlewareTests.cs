using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using TransactionLedger.Domain;
using TransactionLedger.Middleware;

namespace TransactionLedger.Tests.Unit;

/// <summary>
/// Covers K2: a thrown exception returns 500 with no stack trace, no
/// exception message, and no connection string (BR-47).
/// </summary>
public sealed class GlobalExceptionMiddlewareTests
{
    [Fact]
    public async Task Unhandled_exception_returns_500_with_no_internal_detail_leaked()
    {
        var middleware = new GlobalExceptionMiddleware(
            _ => throw new InvalidOperationException("leaked connection string: Host=db;Password=secret"),
            NullLogger<GlobalExceptionMiddleware>.Instance);

        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        await middleware.InvokeAsync(context);
        var (body, raw) = await ReadBodyAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal("INTERNAL_ERROR", body.GetProperty("code").GetString());
        Assert.DoesNotContain("secret", raw);
        Assert.DoesNotContain("Password", raw);
        Assert.DoesNotContain("at TransactionLedger", raw);
        Assert.True(body.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task DomainException_returns_its_own_code_status_and_message()
    {
        var middleware = new GlobalExceptionMiddleware(
            _ => throw new DomainException(
                "INSUFFICIENT_FUNDS",
                StatusCodes.Status409Conflict,
                "Account balance is 50.00; requested debit is 120.00."),
            NullLogger<GlobalExceptionMiddleware>.Instance);

        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        await middleware.InvokeAsync(context);
        var (body, _) = await ReadBodyAsync(context);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.Equal("INSUFFICIENT_FUNDS", body.GetProperty("code").GetString());
        Assert.Equal(
            "Account balance is 50.00; requested debit is 120.00.",
            body.GetProperty("detail").GetString());
    }

    private static async Task<(JsonElement Body, string Raw)> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        var raw = await reader.ReadToEndAsync();
        return (JsonDocument.Parse(raw).RootElement, raw);
    }
}
