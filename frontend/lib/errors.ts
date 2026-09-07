import type { ProblemDetails } from '@/types/api';

/**
 * The error codes from docs/05-api-contract.md §1.2.
 *
 * BR-45: the frontend switches on `code`, never on `title` or `detail`. Those
 * are human text the backend is free to reword; `code` is the contract.
 */
export const ErrorCode = {
  ValidationFailed: 'VALIDATION_FAILED',
  AmountNotPositive: 'AMOUNT_NOT_POSITIVE',
  AmountTooLarge: 'AMOUNT_TOO_LARGE',
  AmountScaleInvalid: 'AMOUNT_SCALE_INVALID',
  DescriptionTooLong: 'DESCRIPTION_TOO_LONG',
  CategorySystemOnly: 'CATEGORY_SYSTEM_ONLY',
  SameAccountTransfer: 'SAME_ACCOUNT_TRANSFER',
  PaginationInvalid: 'PAGINATION_INVALID',
  FilterRangeInvalid: 'FILTER_RANGE_INVALID',
  IdempotencyKeyMissing: 'IDEMPOTENCY_KEY_MISSING',
  InvalidCredentials: 'INVALID_CREDENTIALS',
  Unauthenticated: 'UNAUTHENTICATED',
  NotFound: 'NOT_FOUND',
  EmailAlreadyRegistered: 'EMAIL_ALREADY_REGISTERED',
  AccountNameTaken: 'ACCOUNT_NAME_TAKEN',
  InsufficientFunds: 'INSUFFICIENT_FUNDS',
  AlreadyReversed: 'ALREADY_REVERSED',
  CannotReverseAReversal: 'CANNOT_REVERSE_A_REVERSAL',
  TransferLegNotReversible: 'TRANSFER_LEG_NOT_REVERSIBLE',
  IdempotencyKeyReused: 'IDEMPOTENCY_KEY_REUSED',
  InternalError: 'INTERNAL_ERROR',
} as const;

/**
 * A failed API call, carrying the parsed problem+json body.
 *
 * Thrown rather than returned so that every call site either handles the
 * failure deliberately or lets it reach an error boundary — a returned error
 * object is far too easy to ignore when the value being ignored is money.
 */
export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  readonly detail?: string;
  readonly traceId?: string;
  readonly errors?: Record<string, string[]>;

  constructor(problem: ProblemDetails) {
    super(problem.detail ?? problem.title ?? `Request failed with ${problem.status}.`);
    this.name = 'ApiError';
    this.status = problem.status;
    this.code = problem.code;
    this.detail = problem.detail;
    this.traceId = problem.traceId;
    this.errors = problem.errors;
  }

  is(code: string): boolean {
    return this.code === code;
  }
}

/**
 * Raised when the request never produced a problem+json response at all — the
 * API is down, DNS failed, or CORS rejected it. Distinct from ApiError because
 * there is no `code` to switch on and the remedy is different.
 */
export class NetworkError extends Error {
  constructor(cause?: unknown) {
    super('Could not reach the server. Check that the API is running.');
    this.name = 'NetworkError';
    this.cause = cause;
  }
}

/**
 * Codes whose server-supplied `detail` is better than anything written here.
 *
 * INSUFFICIENT_FUNDS is the important one: the API puts the current balance and
 * the requested amount into `detail` specifically so the UI can render a useful
 * message without a second call (contract §5).
 */
const PREFER_SERVER_DETAIL = new Set<string>([ErrorCode.InsufficientFunds]);

const MESSAGES: Record<string, string> = {
  [ErrorCode.ValidationFailed]: 'Please check the highlighted fields.',
  [ErrorCode.AmountNotPositive]: 'Amount must be greater than zero.',
  [ErrorCode.AmountTooLarge]: 'That amount is too large.',
  [ErrorCode.AmountScaleInvalid]: 'Amount can have at most two decimal places.',
  [ErrorCode.DescriptionTooLong]: 'Description is too long.',
  [ErrorCode.CategorySystemOnly]: 'Transfer and Reversal are assigned by the system.',
  [ErrorCode.SameAccountTransfer]: 'Choose two different accounts.',
  [ErrorCode.PaginationInvalid]: 'That page size is not allowed.',
  [ErrorCode.FilterRangeInvalid]: 'The start of the range must come before the end.',
  [ErrorCode.IdempotencyKeyMissing]: 'Something went wrong submitting that. Please try again.',
  [ErrorCode.InvalidCredentials]: 'Email or password is incorrect.',
  [ErrorCode.Unauthenticated]: 'Your session has expired. Please sign in again.',
  // Never "you do not have access": BR-08 returns 404 for another user's
  // resource precisely so its existence is not confirmed. Saying "forbidden"
  // here would leak exactly what the backend refused to.
  [ErrorCode.NotFound]: 'Not found.',
  [ErrorCode.EmailAlreadyRegistered]: 'An account with this email already exists.',
  [ErrorCode.AccountNameTaken]: 'You already have an account with that name.',
  [ErrorCode.InsufficientFunds]: 'That would overdraw the account.',
  [ErrorCode.AlreadyReversed]: 'This transaction has already been reversed.',
  [ErrorCode.CannotReverseAReversal]: 'A reversal cannot itself be reversed.',
  [ErrorCode.TransferLegNotReversible]: 'Reverse the transfer, not one side of it.',
  [ErrorCode.IdempotencyKeyReused]:
    'This looks like a different transaction reusing an earlier key. Please try again.',
  [ErrorCode.InternalError]: 'Something went wrong on our end.',
};

/** One sentence a person can act on. Never a raw JSON dump. */
export function messageFor(error: unknown): string {
  if (error instanceof NetworkError) {
    return error.message;
  }

  if (error instanceof ApiError) {
    if (PREFER_SERVER_DETAIL.has(error.code) && error.detail) {
      return error.detail;
    }
    return MESSAGES[error.code] ?? 'Something went wrong. Please try again.';
  }

  return 'Something went wrong. Please try again.';
}

/**
 * Field-level messages for a VALIDATION_FAILED response, keyed by the lowercased
 * field name so a form can look them up without caring that ASP.NET Core
 * capitalises model-state keys.
 */
export function fieldErrors(error: unknown): Record<string, string> {
  if (!(error instanceof ApiError) || !error.errors) {
    return {};
  }

  return Object.entries(error.errors).reduce<Record<string, string>>((acc, [field, messages]) => {
    if (messages.length > 0) {
      acc[field.toLowerCase()] = messages[0];
    }
    return acc;
  }, {});
}
