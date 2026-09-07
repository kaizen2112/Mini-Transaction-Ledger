'use client';

import { Suspense, useCallback, useEffect, useMemo, useState } from 'react';
import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import { PageHeader } from '@/components/AppShell';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { EmptyState, ErrorNotice, LoadingState } from '@/components/ui/Feedback';
import { Pagination } from '@/components/ui/Pagination';
import { CreateTransactionDrawer } from '@/components/transactions/CreateTransactionDrawer';
import { ReverseTransactionDialog } from '@/components/transactions/ReverseTransactionDialog';
import { TransactionTable } from '@/components/transactions/TransactionTable';
import {
  TransactionFilters,
  type TransactionFilterState,
} from '@/components/transactions/TransactionFilters';
import { api } from '@/lib/api';
import { ApiError, ErrorCode, messageFor } from '@/lib/errors';
import { DEFAULT_PAGE_SIZE, PAGE_SIZE_OPTIONS, endOfDayUtc, startOfDayUtc, type PageSize } from '@/lib/query';
import { useResource } from '@/lib/useResource';
import type {
  AccountListResponse,
  PagedResponse,
  TransactionCategory,
  TransactionQueryParams,
  TransactionResponse,
  TransactionType,
} from '@/types/api';

/**
 * Returned when no account is selected yet (a brief moment before the account
 * list loads and the first account is chosen as default). Never shown as real
 * data — see the guard on `accounts.data.accounts.length === 0` below for the
 * genuinely-no-accounts case.
 */
const EMPTY_PAGE: PagedResponse<TransactionResponse> = {
  items: [],
  page: 1,
  pageSize: DEFAULT_PAGE_SIZE,
  totalItems: 0,
  totalPages: 0,
};

function parsePageSize(raw: string | null): PageSize {
  const parsed = Number(raw);
  return (PAGE_SIZE_OPTIONS as readonly number[]).includes(parsed) ? (parsed as PageSize) : DEFAULT_PAGE_SIZE;
}

/**
 * The full transaction history per docs/12-frontend-plan.md §4.4: one account
 * at a time (§3.1 — there is no global transaction endpoint, so this page asks
 * the question the API can actually answer), the complete BR-42 filter set,
 * and server-side pagination. Every one of those lives in the URL, so a
 * reload or a shared link reproduces exactly what was on screen.
 */
function TransactionsPageInner() {
  const searchParams = useSearchParams();
  const router = useRouter();
  const pathname = usePathname();

  const fetchAccounts = useCallback((signal: AbortSignal) => api.accounts.list(signal), []);
  const [accounts, reloadAccounts] = useResource<AccountListResponse>(fetchAccounts);

  const [accountId, setAccountId] = useState(() => searchParams.get('accountId') ?? '');
  const [page, setPage] = useState(() => Number(searchParams.get('page') ?? '1') || 1);
  const [pageSize, setPageSize] = useState<PageSize>(() => parsePageSize(searchParams.get('pageSize')));
  const [filters, setFilters] = useState<TransactionFilterState>(() => ({
    type: (searchParams.get('type') as TransactionType | null) ?? '',
    category: (searchParams.get('category') as TransactionCategory | null) ?? '',
    from: searchParams.get('from') ?? '',
    to: searchParams.get('to') ?? '',
    minAmount: searchParams.get('minAmount') ?? '',
    maxAmount: searchParams.get('maxAmount') ?? '',
    search: searchParams.get('search') ?? '',
  }));

  const [drawerOpen, setDrawerOpen] = useState(false);
  const [reversing, setReversing] = useState<TransactionResponse | null>(null);

  // Defaults to the first account once the list loads, if the URL named none.
  // Derived during render rather than in a useEffect — React's documented
  // pattern for reacting to a changed input (react.dev/learn/you-might-not
  // -need-an-effect#adjusting-some-state-when-a-prop-changes) — so the default
  // is already in place on the render that shows the loaded accounts, instead
  // of committing once with no selection and once more a tick later.
  const [seenAccounts, setSeenAccounts] = useState(accounts.data);
  if (accounts.data !== seenAccounts) {
    setSeenAccounts(accounts.data);
    if (!accountId && accounts.data && accounts.data.accounts.length > 0) {
      setAccountId(accounts.data.accounts[0].id);
    }
  }

  // Every piece of state that shapes the request round-trips through the URL,
  // so filters/page/account survive a reload or a shared link (Phase 2 asks
  // for this explicitly).
  useEffect(() => {
    if (!accountId) return;

    const params = new URLSearchParams();
    params.set('accountId', accountId);
    params.set('page', String(page));
    params.set('pageSize', String(pageSize));
    if (filters.type) params.set('type', filters.type);
    if (filters.category) params.set('category', filters.category);
    if (filters.from) params.set('from', filters.from);
    if (filters.to) params.set('to', filters.to);
    if (filters.minAmount) params.set('minAmount', filters.minAmount);
    if (filters.maxAmount) params.set('maxAmount', filters.maxAmount);
    if (filters.search) params.set('search', filters.search);

    router.replace(`${pathname}?${params.toString()}`, { scroll: false });
  }, [accountId, page, pageSize, filters, pathname, router]);

  // A changed filter or account makes the current page number meaningless
  // against the new result set, so both reset to page 1.
  function updateFilters(next: TransactionFilterState) {
    setFilters(next);
    setPage(1);
  }

  // Filters deliberately SURVIVE an account change: "how much did I spend on
  // Food" is a question worth asking of one account after another, and
  // clearing the filter bar underneath the user would throw away what they
  // just typed. Only the page number resets, because page 7 of the old result
  // set means nothing against the new one.
  function changeAccount(next: string) {
    setAccountId(next);
    setPage(1);
  }

  const queryParams = useMemo<TransactionQueryParams>(
    () => ({
      page,
      pageSize,
      type: filters.type === '' ? undefined : filters.type,
      category: filters.category === '' ? undefined : filters.category,
      from: filters.from ? startOfDayUtc(filters.from) : undefined,
      to: filters.to ? endOfDayUtc(filters.to) : undefined,
      minAmount: filters.minAmount ? Number(filters.minAmount) : undefined,
      maxAmount: filters.maxAmount ? Number(filters.maxAmount) : undefined,
      search: filters.search || undefined,
    }),
    [page, pageSize, filters],
  );

  const fetchTransactions = useCallback(
    (signal: AbortSignal) =>
      accountId ? api.transactions.list(accountId, queryParams, signal) : Promise.resolve(EMPTY_PAGE),
    [accountId, queryParams],
  );

  const [transactions, reload] = useResource<PagedResponse<TransactionResponse>>(fetchTransactions);

  if (accounts.status === 'loading') {
    return <LoadingState label="Loading accounts…" />;
  }

  if (accounts.status === 'error' && !accounts.data) {
    return (
      <>
        <PageHeader title="Transactions" subtitle="Log and review money moving in and out of your accounts." />
        <ErrorNotice
          message={messageFor(accounts.error)}
          traceId={accounts.error instanceof ApiError ? accounts.error.traceId : undefined}
          onRetry={reloadAccounts}
        />
      </>
    );
  }

  if (accounts.data && accounts.data.accounts.length === 0) {
    return (
      <>
        <PageHeader title="Transactions" subtitle="Log and review money moving in and out of your accounts." />
        <EmptyState
          title="No accounts yet"
          body="A transaction belongs to an account. Create one first, then come back here to record money in or out."
          action={<Button onClick={() => router.push('/accounts')}>Go to accounts</Button>}
        />
      </>
    );
  }

  // These three read the SAME underlying error object into three different
  // slots — a banner, and two inline messages next to the controls that
  // actually caused them — because BR-45/§5.4 asks for the range and
  // pagination errors to land on the offending control, not a generic banner.
  const rangeError =
    transactions.error instanceof ApiError && transactions.error.is(ErrorCode.FilterRangeInvalid)
      ? messageFor(transactions.error)
      : undefined;

  const paginationError =
    transactions.error instanceof ApiError && transactions.error.is(ErrorCode.PaginationInvalid)
      ? messageFor(transactions.error)
      : undefined;

  const genericError =
    transactions.status === 'error' && !rangeError && !paginationError ? messageFor(transactions.error) : undefined;

  return (
    <>
      <PageHeader
        title="Transactions"
        subtitle="Log and review money moving in and out of your accounts."
        action={accountId ? <Button onClick={() => setDrawerOpen(true)}>+ Add transaction</Button> : undefined}
      />

      <div className="mb-4 flex items-center gap-2">
        <label htmlFor="account-select" className="text-sm font-medium text-ink-soft">
          Account
        </label>
        <select
          id="account-select"
          value={accountId}
          onChange={(e) => changeAccount(e.target.value)}
          className="rounded-lg border-2 border-accent bg-accent-tint px-3 py-1.5 text-sm font-medium text-accent focus:outline-2 focus:outline-brand"
        >
          {accounts.data?.accounts.map((account) => (
            <option key={account.id} value={account.id}>
              {account.name}
            </option>
          ))}
        </select>
      </div>

      {genericError && (
        <div className="mb-4">
          <ErrorNotice
            message={genericError}
            traceId={transactions.error instanceof ApiError ? transactions.error.traceId : undefined}
            onRetry={reload}
          />
        </div>
      )}

      <Card>
        <TransactionFilters value={filters} onChange={updateFilters} rangeError={rangeError} />

        {paginationError && (
          <p role="alert" className="px-4 pt-3 text-xs text-debit">
            {paginationError}
          </p>
        )}

        {transactions.status === 'loading' && <LoadingState label="Loading transactions…" />}

        {transactions.data && transactions.data.items.length === 0 && (
          <p className="px-4 py-10 text-center text-sm text-ink-faint">
            No transactions match these filters.
          </p>
        )}

        {transactions.data && transactions.data.items.length > 0 && (
          <>
            <TransactionTable items={transactions.data.items} onReverse={setReversing} />
            <Pagination
              page={transactions.data.page}
              pageSize={transactions.data.pageSize}
              totalItems={transactions.data.totalItems}
              totalPages={transactions.data.totalPages}
              onPageChange={setPage}
              onPageSizeChange={(size) => {
                setPageSize(size);
                setPage(1);
              }}
            />
          </>
        )}
      </Card>

      {accountId && (
        <CreateTransactionDrawer
          open={drawerOpen}
          accountId={accountId}
          onClose={() => setDrawerOpen(false)}
          onCreated={reload}
        />
      )}

      <ReverseTransactionDialog transaction={reversing} onClose={() => setReversing(null)} onReversed={reload} />
    </>
  );
}

export default function TransactionsPage() {
  // useSearchParams requires a Suspense boundary in the App Router (same
  // reason /login wraps its form).
  return (
    <Suspense fallback={<LoadingState />}>
      <TransactionsPageInner />
    </Suspense>
  );
}
