import { ApiError, ErrorCode, NetworkError } from '@/lib/errors';
import { toQueryString } from '@/lib/query';
import { clearSession, getToken } from '@/lib/session';
import type {
  AccountBalanceResponse,
  AccountListResponse,
  AccountResponse,
  AccountType,
  CategoryReportQueryParams,
  CategoryReportResponse,
  CreateTransactionRequest,
  CreateTransferRequest,
  LoginResponse,
  MonthlyReportQueryParams,
  MonthlyReportResponse,
  PagedResponse,
  ProblemDetails,
  RegisterResponse,
  ReverseTransactionRequest,
  SummaryReportQueryParams,
  SummaryReportResponse,
  TransactionQueryParams,
  TransactionResponse,
  TransferQueryParams,
  TransferResponse,
} from '@/types/api';

/**
 * The single network layer, per docs/12-frontend-plan.md §5.1.
 *
 * Every request in the app goes through `request()`. That is the point: bearer
 * injection, problem+json parsing and 401 handling are written once and cannot
 * be forgotten by a call site.
 *
 * Only NEXT_PUBLIC_API_BASE_URL is used. The app is client-rendered because the
 * token lives in the browser (§5.2), so the server-side API_BASE_URL from
 * docs/08-docker.md §2 has no consumer yet.
 */

const BASE_URL = (process.env.NEXT_PUBLIC_API_BASE_URL ?? 'http://localhost:8080').replace(
  /\/+$/,
  '',
);

export const IDEMPOTENT_REPLAY_HEADER = 'Idempotent-Replay';
export const IDEMPOTENCY_KEY_HEADER = 'Idempotency-Key';

interface RequestOptions {
  method?: 'GET' | 'POST';
  body?: unknown;
  /** Money-moving POSTs only (BR-32). See §5.3 for the key's lifecycle. */
  idempotencyKey?: string;
  signal?: AbortSignal;
}

/** A response plus the one header the UI needs to read off it. */
export interface ApiResult<T> {
  data: T;
  /** True when the server replayed a previous identical request (BR-34). */
  isReplay: boolean;
}

async function request<T>(path: string, options: RequestOptions = {}): Promise<ApiResult<T>> {
  const { method = 'GET', body, idempotencyKey, signal } = options;

  const headers = new Headers({ Accept: 'application/json, application/problem+json' });

  const token = getToken();
  if (token) {
    headers.set('Authorization', `Bearer ${token}`);
  }
  if (body !== undefined) {
    headers.set('Content-Type', 'application/json');
  }
  if (idempotencyKey) {
    headers.set(IDEMPOTENCY_KEY_HEADER, idempotencyKey);
  }

  let response: Response;
  try {
    response = await fetch(`${BASE_URL}${path}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      signal,
    });
  } catch (cause) {
    // fetch() rejects only on a transport-level failure: the API is down, or
    // CORS rejected the request before any status was produced. A 500 is a
    // resolved promise, not a rejection, and is handled below.
    if (cause instanceof DOMException && cause.name === 'AbortError') throw cause;
    throw new NetworkError(cause);
  }

  if (!response.ok) {
    throw await toApiError(response);
  }

  return {
    data: await readBody<T>(response),
    isReplay: response.headers.get(IDEMPOTENT_REPLAY_HEADER)?.toLowerCase() === 'true',
  };
}

async function readBody<T>(response: Response): Promise<T> {
  // 204, or any response the endpoint documents as empty.
  if (response.status === 204 || response.headers.get('Content-Length') === '0') {
    return undefined as T;
  }

  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

async function toApiError(response: Response): Promise<ApiError> {
  let problem: ProblemDetails;

  try {
    const parsed: unknown = JSON.parse(await response.text());
    problem =
      typeof parsed === 'object' && parsed !== null && 'code' in parsed
        ? (parsed as ProblemDetails)
        : fallbackProblem(response.status);
  } catch {
    // A non-JSON error body means something outside the app produced it — a
    // proxy, or the server dying before the exception middleware ran.
    problem = fallbackProblem(response.status);
  }

  // 401 is handled centrally: the token is gone or expired, so no call site
  // has to think about it.
  if (response.status === 401 && typeof window !== 'undefined') {
    clearSession();
    if (!window.location.pathname.startsWith('/login')) {
      // A FULL page load, not router.push, and deliberately so. The session is
      // gone, so every mounted component is holding data fetched with a token
      // that no longer works. A client-side navigation would keep that tree
      // alive and let stale balances stay on screen behind the login page;
      // a hard navigation throws all of it away. This module is also not a
      // component, so there is no router here to call.
      // eslint-disable-next-line @next/next/no-location-assign-relative-destination
      window.location.href = '/login?expired=1';
    }
  }

  return new ApiError(problem);
}

function fallbackProblem(status: number): ProblemDetails {
  return {
    status,
    code: status === 401 ? ErrorCode.Unauthenticated : ErrorCode.InternalError,
  };
}

/** Unwraps to the body for the majority of calls that cannot be replays. */
async function get<T>(path: string, signal?: AbortSignal): Promise<T> {
  return (await request<T>(path, { signal })).data;
}

/**
 * Query-string params are widened to `string | number | undefined` so
 * `toQueryString` can drop the unset ones; the DTOs above keep the narrower
 * enum types for callers.
 */
function toParams<T extends object>(query: T): Record<string, string | number | undefined> {
  const params: Record<string, string | number | undefined> = {};
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== null && value !== '') {
      params[key] = value as string | number;
    }
  }
  return params;
}

// ---------------------------------------------------------------- endpoints
//
// One function per endpoint in docs/05-api-contract.md §9. Phase 1 covers auth
// and accounts; transactions and transfers arrive here in phase 2, reports in
// phase 3.

export const api = {
  auth: {
    /** Returns no token by design (contract §3) — register then sign in. */
    register: (body: { email: string; password: string; displayName: string }) =>
      request<RegisterResponse>('/api/auth/register', { method: 'POST', body }).then((r) => r.data),

    login: (body: { email: string; password: string }) =>
      request<LoginResponse>('/api/auth/login', { method: 'POST', body }).then((r) => r.data),
  },

  accounts: {
    list: (signal?: AbortSignal) => get<AccountListResponse>('/api/accounts', signal),

    get: (accountId: string, signal?: AbortSignal) =>
      get<AccountResponse>(`/api/accounts/${accountId}`, signal),

    balance: (accountId: string, signal?: AbortSignal) =>
      get<AccountBalanceResponse>(`/api/accounts/${accountId}/balance`, signal),

    /**
     * No Idempotency-Key: a duplicate submission is already blocked by the
     * unique name constraint, which returns 409 ACCOUNT_NAME_TAKEN (BR-32).
     */
    create: (body: { name: string; type: AccountType }) =>
      request<AccountResponse>('/api/accounts', { method: 'POST', body }).then((r) => r.data),
  },

  transactions: {
    /**
     * Returns the full ApiResult, not just the body: the caller needs
     * `isReplay` to render the 201-vs-200 replay exactly the same way (§5.3)
     * rather than reporting "created" twice.
     */
    create: (accountId: string, body: CreateTransactionRequest, idempotencyKey: string) =>
      request<TransactionResponse>(`/api/accounts/${accountId}/transactions`, {
        method: 'POST',
        body,
        idempotencyKey,
      }),

    list: (accountId: string, query: TransactionQueryParams, signal?: AbortSignal) =>
      get<PagedResponse<TransactionResponse>>(
        `/api/accounts/${accountId}/transactions${toQueryString(toParams(query))}`,
        signal,
      ),

    get: (transactionId: string, signal?: AbortSignal) =>
      get<TransactionResponse>(`/api/transactions/${transactionId}`, signal),

    /**
     * No Idempotency-Key: the unique index on ReversesTransactionId already
     * makes a repeat impossible by construction, returning 409
     * ALREADY_REVERSED instead (contract §5).
     */
    reverse: (transactionId: string, body: ReverseTransactionRequest) =>
      request<TransactionResponse>(`/api/transactions/${transactionId}/reverse`, {
        method: 'POST',
        body,
      }).then((r) => r.data),
  },

  transfers: {
    /** Same isReplay contract as transactions.create — see the note there. */
    create: (body: CreateTransferRequest, idempotencyKey: string) =>
      request<TransferResponse>('/api/transfers', { method: 'POST', body, idempotencyKey }),

    list: (query: TransferQueryParams, signal?: AbortSignal) =>
      get<PagedResponse<TransferResponse>>(
        `/api/transfers${toQueryString(toParams(query))}`,
        signal,
      ),
  },

  /**
   * Read-only aggregates, all computed in SQL over the caller's own accounts
   * (BR-40/BR-43). Nothing here can change data, which is why none of them
   * takes an idempotency key.
   */
  reports: {
    /**
     * NOTE the asymmetry, which is deliberate and must not be "fixed" in the
     * UI: totalBalance is the CURRENT sum of account balances and ignores
     * from/to entirely, while totalCredits/totalDebits/netChange respect it.
     * "What do I have" and "what happened in August" are different questions
     * (contract §7).
     */
    summary: (query: SummaryReportQueryParams, signal?: AbortSignal) =>
      get<SummaryReportResponse>(`/api/reports/summary${toQueryString(toParams(query))}`, signal),

    /**
     * Debits only, excluding transfer legs, reversals, and the transactions
     * they reverse (BR-43). Rows arrive ordered biggest-first, so the chart
     * renders them in the order given and never re-sorts.
     */
    categories: (query: CategoryReportQueryParams, signal?: AbortSignal) =>
      get<CategoryReportResponse>(`/api/reports/categories${toQueryString(toParams(query))}`, signal),

    monthly: (query: MonthlyReportQueryParams, signal?: AbortSignal) =>
      get<MonthlyReportResponse>(`/api/reports/monthly${toQueryString(toParams(query))}`, signal),
  },
};

export { request };
