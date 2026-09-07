/**
 * Mirrors of the DTOs in backend/TransactionLedger/DTOs/.
 *
 * Hand-written rather than generated from the OpenAPI document. The cost is
 * that these must be kept in step with the C# records by hand; the benefit is
 * that the whole wire format is one readable file with no build step.
 *
 * Enums are string-literal unions because the API serialises enum NAMES in both
 * directions (docs/05-api-contract.md §1.3). The integer storage in §04 is an
 * implementation detail that never reaches this layer.
 */

// ---------------------------------------------------------------- enums

export const ACCOUNT_TYPES = ['Cash', 'Savings', 'Business', 'Wallet', 'Other'] as const;
export type AccountType = (typeof ACCOUNT_TYPES)[number];

export const TRANSACTION_TYPES = ['Credit', 'Debit'] as const;
export type TransactionType = (typeof TRANSACTION_TYPES)[number];

/**
 * Transfer and Reversal are system-only (BR-24): the backend assigns them and
 * rejects them as input with 400 CATEGORY_SYSTEM_ONLY. They are still valid
 * values to FILTER history by, so both lists exist separately below.
 */
export const TRANSACTION_CATEGORIES = [
  'Salary',
  'Food',
  'Transport',
  'Bills',
  'Shopping',
  'Entertainment',
  'Health',
  'Other',
  'Transfer',
  'Reversal',
] as const;
export type TransactionCategory = (typeof TRANSACTION_CATEGORIES)[number];

/** The subset a user may assign when creating a transaction (BR-24). */
export const ASSIGNABLE_CATEGORIES = TRANSACTION_CATEGORIES.filter(
  (category) => category !== 'Transfer' && category !== 'Reversal',
) as readonly Exclude<TransactionCategory, 'Transfer' | 'Reversal'>[];

// ---------------------------------------------------------------- auth

export interface UserSummary {
  id: string;
  email: string;
  displayName: string;
}

export interface LoginResponse {
  accessToken: string;
  /** ISO 8601 UTC. 60 minutes out; there is no refresh token (contract §3). */
  expiresAt: string;
  user: UserSummary;
}

export interface RegisterResponse {
  id: string;
  email: string;
  displayName: string;
  createdAt: string;
}

// ---------------------------------------------------------------- accounts

export interface AccountResponse {
  id: string;
  name: string;
  type: AccountType;
  /** JSON number with two decimals. Never do arithmetic on this in the UI. */
  balance: number;
  createdAt: string;
}

/** Not paginated: the realistic upper bound is a handful of accounts. */
export interface AccountListResponse {
  accounts: AccountResponse[];
  totalBalance: number;
}

export interface AccountBalanceResponse {
  accountId: string;
  balance: number;
  asOf: string;
}

// ---------------------------------------------------------------- transactions

export interface TransactionResponse {
  id: string;
  accountId: string;
  type: TransactionType;
  amount: number;
  category: TransactionCategory;
  description: string | null;
  occurredAt: string;
  /** Set when THIS row is a reversal of another. */
  reversesTransactionId: string | null;
  /** Computed by the query, not stored — the original row is never mutated (BR-21). */
  isReversed: boolean;
  /** Set when this row is one leg of a transfer. Such a leg is not reversible. */
  transferId: string | null;
}

export interface TransferResponse {
  id: string;
  sourceAccountId: string;
  destinationAccountId: string;
  amount: number;
  debitTransactionId: string;
  creditTransactionId: string;
  occurredAt: string;
}

export interface PagedResponse<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
}

// ---------------------------------------------------------------- reports

export interface SummaryReportResponse {
  /** Current sum of all account balances. Deliberately ignores from/to. */
  totalBalance: number;
  totalCredits: number;
  totalDebits: number;
  netChange: number;
  transactionCount: number;
  accountCount: number;
  from: string | null;
  to: string | null;
}

export interface CategoryReportItem {
  category: TransactionCategory;
  totalAmount: number;
  transactionCount: number;
}

/** Already ordered biggest-first by the API; do not re-sort. */
export interface CategoryReportResponse {
  categories: CategoryReportItem[];
  totalAmount: number;
}

export interface MonthlyReportItem {
  year: number;
  /** 1-12. */
  month: number;
  credits: number;
  debits: number;
  net: number;
}

export interface MonthlyReportResponse {
  months: MonthlyReportItem[];
}

// ---------------------------------------------------------------- errors

/**
 * The RFC 7807 envelope from contract §1.1. `code` is the stable member the UI
 * switches on (BR-45); `title` and `detail` are human text that may change.
 */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status: number;
  code: string;
  detail?: string;
  traceId?: string;
  /** Present on VALIDATION_FAILED: field name -> messages. */
  errors?: Record<string, string[]>;
}

// ---------------------------------------------------------------- transaction/transfer requests

/** POST /api/accounts/{accountId}/transactions body (contract §5). */
export interface CreateTransactionRequest {
  type: TransactionType;
  amount: number;
  /** Must be one of ASSIGNABLE_CATEGORIES — Transfer/Reversal are system-only (BR-24). */
  category: TransactionCategory;
  description?: string;
}

/** POST /api/transfers body (contract §6). */
export interface CreateTransferRequest {
  sourceAccountId: string;
  destinationAccountId: string;
  amount: number;
  description?: string;
}

/** Optional POST /api/transactions/{id}/reverse body (contract §5). */
export interface ReverseTransactionRequest {
  description?: string;
}

/**
 * GET /api/accounts/{accountId}/transactions query (contract §5, BR-42).
 * Every field is optional except pagination, which always carries a value —
 * see PAGE_SIZE_OPTIONS in lib/query.ts for the three allowed page sizes.
 */
export interface TransactionQueryParams {
  page: number;
  pageSize: number;
  type?: TransactionType;
  category?: TransactionCategory;
  /** ISO 8601. Inclusive (BR-42). */
  from?: string;
  to?: string;
  minAmount?: number;
  maxAmount?: number;
  search?: string;
}

/** GET /api/transfers query — pagination only, no filters (contract §6). */
export interface TransferQueryParams {
  page: number;
  pageSize: number;
}

// ---------------------------------------------------------------- report queries

/** GET /api/reports/summary query (contract §7). */
export interface SummaryReportQueryParams {
  /** ISO 8601. Bounds the credit/debit/net figures — NOT totalBalance. */
  from?: string;
  to?: string;
}

/** GET /api/reports/categories query (contract §7). */
export interface CategoryReportQueryParams {
  from?: string;
  to?: string;
  /** Optional narrowing. Another user's account is a 404, not an empty report (BR-08). */
  accountId?: string;
}

/** GET /api/reports/monthly query (contract §7). Omitted year = trailing 12 months. */
export interface MonthlyReportQueryParams {
  year?: number;
}
