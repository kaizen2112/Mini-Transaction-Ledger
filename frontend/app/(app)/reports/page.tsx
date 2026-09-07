'use client';

import { useCallback, useMemo, useState } from 'react';
import { PageHeader } from '@/components/AppShell';
import { Card, StatCard } from '@/components/ui/Card';
import { EmptyState, ErrorNotice } from '@/components/ui/Feedback';
import { ChartSkeleton, StatCardSkeleton } from '@/components/ui/Skeleton';
import { CategoryBars } from '@/components/charts/CategoryBars';
import { MonthlyBars } from '@/components/charts/MonthlyBars';
import { api } from '@/lib/api';
import { ApiError, ErrorCode, messageFor } from '@/lib/errors';
import { formatMoney, formatMonth } from '@/lib/format';
import { endOfDayUtc, startOfDayUtc } from '@/lib/query';
import { useResource } from '@/lib/useResource';
import type {
  AccountListResponse,
  CategoryReportResponse,
  MonthlyReportResponse,
  SummaryReportResponse,
} from '@/types/api';

const CONTROL =
  'rounded-lg border border-line-strong bg-surface px-3 py-1.5 text-sm text-ink ' +
  'focus:outline-2 focus:outline-brand';

/** Enough to cover a backdated import without offering all 8,000 years the API allows. */
function yearOptions(): number[] {
  const thisYear = new Date().getUTCFullYear();
  return Array.from({ length: 6 }, (_, index) => thisYear - index);
}

/**
 * /reports per docs/12-frontend-plan.md §4.6 — three sections, each a direct
 * render of one endpoint, each loading independently so one failure does not
 * blank the other two.
 *
 * The date range is hoisted above Summary and Categories because it is the
 * SAME from/to parameter on both endpoints; duplicating the picker would
 * invite the two halves of one page to disagree about which period they show.
 * Monthly keeps its own year selector because it takes a different parameter
 * entirely.
 */
export default function ReportsPage() {
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [accountId, setAccountId] = useState('');
  const [year, setYear] = useState<string>('');

  const range = useMemo(
    () => ({
      from: from ? startOfDayUtc(from) : undefined,
      to: to ? endOfDayUtc(to) : undefined,
    }),
    [from, to],
  );

  const fetchAccounts = useCallback((signal: AbortSignal) => api.accounts.list(signal), []);
  const [accounts] = useResource<AccountListResponse>(fetchAccounts);

  const fetchSummary = useCallback(
    (signal: AbortSignal) => api.reports.summary(range, signal),
    [range],
  );
  const [summary, reloadSummary] = useResource<SummaryReportResponse>(fetchSummary);

  const fetchCategories = useCallback(
    (signal: AbortSignal) =>
      api.reports.categories({ ...range, accountId: accountId || undefined }, signal),
    [range, accountId],
  );
  const [categories, reloadCategories] = useResource<CategoryReportResponse>(fetchCategories);

  const fetchMonthly = useCallback(
    (signal: AbortSignal) => api.reports.monthly({ year: year ? Number(year) : undefined }, signal),
    [year],
  );
  const [monthly, reloadMonthly] = useResource<MonthlyReportResponse>(fetchMonthly);

  // An inverted range is a 400 from every endpoint that takes one. Shown once,
  // under the controls that caused it, rather than as three identical banners
  // (§5.4: inline on the offending control).
  const rangeError =
    summary.error instanceof ApiError && summary.error.is(ErrorCode.FilterRangeInvalid)
      ? messageFor(summary.error)
      : undefined;

  // BR-08: a 404 here means the accountId names an account that is not yours —
  // indistinguishable, deliberately, from one that does not exist.
  const foreignAccount =
    categories.error instanceof ApiError && categories.error.is(ErrorCode.NotFound);

  return (
    <>
      <PageHeader
        title="Reports"
        subtitle="Turn your ledger into totals: what you have, where it went, and how the months compare."
      />

      {/* ------------------------------------------------ shared date range */}
      <Card className="mb-6 px-4 py-3">
        <div className="flex flex-wrap items-end gap-3">
          <label className="text-xs text-ink-faint">
            From
            <input
              type="date"
              value={from}
              onChange={(event) => setFrom(event.target.value)}
              className={`${CONTROL} mt-1 block`}
            />
          </label>
          <label className="text-xs text-ink-faint">
            To
            <input
              type="date"
              value={to}
              onChange={(event) => setTo(event.target.value)}
              className={`${CONTROL} mt-1 block`}
            />
          </label>
          {(from || to) && (
            <button
              type="button"
              onClick={() => {
                setFrom('');
                setTo('');
              }}
              className="pb-1.5 text-sm font-medium text-ink-soft underline underline-offset-2 hover:text-ink"
            >
              Clear range
            </button>
          )}
          <p className="pb-1.5 text-xs text-ink-faint">Applies to the summary and category sections.</p>
        </div>

        {rangeError && (
          <p role="alert" className="mt-2 text-xs text-debit">
            {rangeError}
          </p>
        )}
      </Card>

      {/* ------------------------------------------------------- 1. summary */}
      <section className="mb-8">
        <h2 className="mb-3 text-sm font-medium text-ink">Summary</h2>

        {summary.status === 'loading' && (
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <StatCardSkeleton />
            <StatCardSkeleton />
            <StatCardSkeleton />
            <StatCardSkeleton />
          </div>
        )}

        {summary.status === 'error' && !rangeError && (
          <ErrorNotice
            message={messageFor(summary.error)}
            traceId={summary.error instanceof ApiError ? summary.error.traceId : undefined}
            onRetry={reloadSummary}
          />
        )}

        {summary.data && (
          <>
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
              {/*
                Total balance deliberately ignores the date range above, while
                the three figures beside it respect it. "What do I have" is a
                question about now; "what happened in August" is a question
                about a window. The sublabels say so on screen so the
                difference reads as intent rather than a bug (contract §7).
              */}
              <StatCard
                label="Total balance"
                sublabel="Now — ignores the date range"
                value={formatMoney(summary.data.totalBalance)}
              />
              <StatCard
                label="Credits"
                sublabel={from || to ? 'In range' : 'All time'}
                value={formatMoney(summary.data.totalCredits)}
                tone="credit"
              />
              <StatCard
                label="Debits"
                sublabel={from || to ? 'In range' : 'All time'}
                value={formatMoney(summary.data.totalDebits)}
                tone="debit"
              />
              <StatCard
                label="Net change"
                sublabel={from || to ? 'In range' : 'All time'}
                value={formatMoney(summary.data.netChange)}
                tone={summary.data.netChange >= 0 ? 'credit' : 'debit'}
              />
            </div>

            <p className="mt-2 text-xs text-ink-faint">
              {summary.data.transactionCount} transaction
              {summary.data.transactionCount === 1 ? '' : 's'} across{' '}
              {summary.data.accountCount} account
              {summary.data.accountCount === 1 ? '' : 's'}. Credits, debits and net include
              transfer legs and reversals — they are real ledger activity.
            </p>
          </>
        )}
      </section>

      {/* ---------------------------------------------------- 2. categories */}
      <section className="mb-8">
        <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-sm font-medium text-ink">Spending by category</h2>
          <label className="flex items-center gap-2 text-xs text-ink-faint">
            Account
            <select
              value={accountId}
              onChange={(event) => setAccountId(event.target.value)}
              className={CONTROL}
            >
              <option value="">All accounts</option>
              {accounts.data?.accounts.map((account) => (
                <option key={account.id} value={account.id}>
                  {account.name}
                </option>
              ))}
            </select>
          </label>
        </div>

        <Card className="px-5 py-5">
          {categories.status === 'loading' && <ChartSkeleton />}

          {foreignAccount && (
            <div className="space-y-3">
              <ErrorNotice message="That account was not found." />
              <button
                type="button"
                onClick={() => setAccountId('')}
                className="text-sm font-medium text-brand underline underline-offset-2"
              >
                Show all accounts instead
              </button>
            </div>
          )}

          {categories.status === 'error' && !foreignAccount && !rangeError && (
            <ErrorNotice
              message={messageFor(categories.error)}
              traceId={categories.error instanceof ApiError ? categories.error.traceId : undefined}
              onRetry={reloadCategories}
            />
          )}

          {categories.data && categories.data.categories.length === 0 && !foreignAccount && (
            <p className="py-6 text-center text-sm text-ink-faint">
              No spending in this period. Only debits count as spending, so an account holding
              nothing but income shows nothing here.
            </p>
          )}

          {categories.data && categories.data.categories.length > 0 && (
            <>
              <CategoryBars items={categories.data.categories} />
              <p className="mt-4 border-t border-line pt-3 text-sm text-ink">
                Total spending{' '}
                <span className="tabular font-medium">{formatMoney(categories.data.totalAmount)}</span>
              </p>
            </>
          )}

          {/*
            The explanatory line §4.6 asks for, and the most load-bearing
            sentence on the page: without it these numbers look like they
            disagree with the summary above, when in fact the two are answering
            different questions (BR-43).
          */}
          <p className="mt-3 text-xs text-ink-faint">
            Spending only. Transfers between your own accounts and reversed transactions are
            excluded.
          </p>
        </Card>
      </section>

      {/* ------------------------------------------------------- 3. monthly */}
      <section>
        <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-sm font-medium text-ink">Income vs expenses by month</h2>
          <label className="flex items-center gap-2 text-xs text-ink-faint">
            Year
            <select
              value={year}
              onChange={(event) => setYear(event.target.value)}
              className={CONTROL}
            >
              <option value="">Last 12 months</option>
              {yearOptions().map((option) => (
                <option key={option} value={option}>
                  {option}
                </option>
              ))}
            </select>
          </label>
        </div>

        <Card className="px-5 py-5">
          {monthly.status === 'loading' && <ChartSkeleton rows={4} />}

          {monthly.status === 'error' && (
            <ErrorNotice
              message={messageFor(monthly.error)}
              traceId={monthly.error instanceof ApiError ? monthly.error.traceId : undefined}
              onRetry={reloadMonthly}
            />
          )}

          {monthly.data && monthly.data.months.length === 0 && (
            <p className="py-6 text-center text-sm text-ink-faint">
              No activity in this period.
            </p>
          )}

          {monthly.data && monthly.data.months.length > 0 && (
            <>
              <MonthlyBars months={monthly.data.months} />

              <div className="mt-5 overflow-x-auto border-t border-line pt-3">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="text-left text-xs uppercase tracking-wide text-ink-faint">
                      <th className="py-2 font-medium">Month</th>
                      <th className="py-2 text-right font-medium">Credits</th>
                      <th className="py-2 text-right font-medium">Debits</th>
                      <th className="py-2 text-right font-medium">Net</th>
                    </tr>
                  </thead>
                  <tbody>
                    {monthly.data.months.map((month) => (
                      <tr key={`${month.year}-${month.month}`} className="border-t border-line">
                        <td className="py-2 text-ink">{formatMonth(month.year, month.month)}</td>
                        <td className="tabular py-2 text-right text-credit">
                          {formatMoney(month.credits)}
                        </td>
                        <td className="tabular py-2 text-right text-debit">
                          {formatMoney(month.debits)}
                        </td>
                        <td
                          className={`tabular py-2 text-right font-medium ${
                            month.net >= 0 ? 'text-credit' : 'text-debit'
                          }`}
                        >
                          {formatMoney(month.net)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </>
          )}
        </Card>
      </section>

      {summary.data && summary.data.accountCount === 0 && (
        <div className="mt-8">
          <EmptyState
            title="Nothing to report yet"
            body="Reports are computed from your accounts and transactions. Once you have recorded a few, this page fills in."
          />
        </div>
      )}
    </>
  );
}
