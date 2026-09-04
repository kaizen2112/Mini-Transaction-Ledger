namespace TransactionLedger.Domain;

/// <summary>
/// The stable machine-readable codes from docs/05-api-contract.md §1.2. The
/// frontend switches on these, never on title or detail (BR-45), so they are
/// constants rather than inline strings.
/// </summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string Unauthenticated = "UNAUTHENTICATED";
    public const string EmailAlreadyRegistered = "EMAIL_ALREADY_REGISTERED";
    public const string InternalError = "INTERNAL_ERROR";
}
