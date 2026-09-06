namespace TransactionLedger.Domain;

/// <summary>
/// The stable machine-readable codes from docs/05-api-contract.md §1.2. The
/// frontend switches on these, never on title or detail (BR-45), so they are
/// constants rather than inline strings.
/// </summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string AmountNotPositive = "AMOUNT_NOT_POSITIVE";
    public const string AmountTooLarge = "AMOUNT_TOO_LARGE";
    public const string AmountScaleInvalid = "AMOUNT_SCALE_INVALID";
    public const string DescriptionTooLong = "DESCRIPTION_TOO_LONG";
    public const string CategorySystemOnly = "CATEGORY_SYSTEM_ONLY";
    public const string PaginationInvalid = "PAGINATION_INVALID";
    public const string FilterRangeInvalid = "FILTER_RANGE_INVALID";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string Unauthenticated = "UNAUTHENTICATED";
    public const string NotFound = "NOT_FOUND";
    public const string EmailAlreadyRegistered = "EMAIL_ALREADY_REGISTERED";
    public const string AccountNameTaken = "ACCOUNT_NAME_TAKEN";
    public const string InsufficientFunds = "INSUFFICIENT_FUNDS";
    public const string SameAccountTransfer = "SAME_ACCOUNT_TRANSFER";
    public const string IdempotencyKeyMissing = "IDEMPOTENCY_KEY_MISSING";
    public const string IdempotencyKeyReused = "IDEMPOTENCY_KEY_REUSED";
    public const string AlreadyReversed = "ALREADY_REVERSED";
    public const string CannotReverseAReversal = "CANNOT_REVERSE_A_REVERSAL";
    public const string TransferLegNotReversible = "TRANSFER_LEG_NOT_REVERSIBLE";
    public const string InternalError = "INTERNAL_ERROR";
}
