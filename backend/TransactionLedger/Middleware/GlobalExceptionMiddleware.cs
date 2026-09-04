using TransactionLedger.Domain;

namespace TransactionLedger.Middleware;

/// <summary>
/// Outermost pipeline entry: any exception thrown by anything below it
/// becomes an RFC 7807 problem+json body (BR-45) with a traceId, and never
/// leaks a stack trace, SQL, or a connection string (BR-47). A DomainException
/// carries its own code/status; anything else is logged in full server-side
/// and returned to the caller as a generic 500 INTERNAL_ERROR (BR-46).
/// </summary>
public sealed class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (DomainException ex)
        {
            _logger.LogWarning(ex, "Domain exception {Code}: {Message}", ex.Code, ex.Message);
            await ProblemResponse.WriteAsync(context, ex.StatusCode, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception. TraceId={TraceId}", context.TraceIdentifier);
            await ProblemResponse.WriteAsync(
                context,
                StatusCodes.Status500InternalServerError,
                ErrorCodes.InternalError,
                "An unexpected error occurred.");
        }
    }
}
