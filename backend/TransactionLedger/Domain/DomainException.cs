namespace TransactionLedger.Domain;

/// <summary>
/// A business-rule failure a service raises without knowing about HTTP.
/// The middleware translates Code/StatusCode into the problem+json response
/// (BR-45); services never construct an IActionResult or ProblemDetails.
/// </summary>
public class DomainException : Exception
{
    public string Code { get; }
    public int StatusCode { get; }

    public DomainException(string code, int statusCode, string message)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }
}
