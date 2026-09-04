using System.Text.Json;

namespace TransactionLedger.Middleware;

/// <summary>
/// One definition of the error envelope from docs/05-api-contract.md §1.1,
/// shared by the exception middleware, the 401 challenge, and model
/// validation. If the shape were written in three places it would drift in
/// three directions, and the frontend switches on `code` (BR-45).
/// </summary>
public static class ProblemResponse
{
    public const string ContentType = "application/problem+json";

    public static Dictionary<string, object?> Build(
        int statusCode,
        string code,
        string detail,
        string traceId,
        IDictionary<string, string[]>? errors = null)
    {
        var problem = new Dictionary<string, object?>
        {
            ["type"] = $"https://httpstatuses.io/{statusCode}",
            ["title"] = ReasonPhrase(statusCode),
            ["status"] = statusCode,
            ["code"] = code,
            ["detail"] = detail,
            ["traceId"] = traceId
        };

        if (errors is not null)
        {
            problem["errors"] = errors;
        }

        return problem;
    }

    public static async Task WriteAsync(
        HttpContext context,
        int statusCode,
        string code,
        string detail)
    {
        context.Response.ContentType = ContentType;
        context.Response.StatusCode = statusCode;

        var problem = Build(statusCode, code, detail, context.TraceIdentifier);
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
    }

    public static string ReasonPhrase(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "One or more validation errors occurred.",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status422UnprocessableEntity => "Unprocessable Entity",
        _ => "An error occurred"
    };
}
