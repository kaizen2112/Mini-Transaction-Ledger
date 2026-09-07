'use client';

import { useCallback, useMemo } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { PageHeader } from '@/components/AppShell';
import { Button } from '@/components/ui/Button';
import { Card, StatCard } from '@/components/ui/Card';
import { DeltaBadge } from '@/components/ui/DeltaBadge';
import { EmptyState, ErrorNotice } from '@/components/ui/Feedback';
import { ChartSkeleton, Skeleton, StatCardSkeleton } from '@/components/ui/Skeleton';
import { CategoryBars } from '@/components/charts/CategoryBars';
import { MonthlyBars } from '@/components/charts/MonthlyBars';
import { api } from '@/lib/api';
import { ApiError, messageFor } from '@/lib/errors';
import { formatMoney } from '@/lib/format';
import { useAuth } from '@/lib/auth';
import { useResource } from '@/lib/useResource';
import type {
  AccountListResponse,
  CategoryReportResponse,
  MonthlyReportResponse,
  SummaryReportResponse,
} from '@/types/api';

/** The UTC window covering the current calendar month so far. */
function thisMonthRange(): { from: string; to: string } {
  const now = new Date();
  const first = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), 1));
  return { from: first.toISOString(), to: now.toISOString() };
}

/**
 * The dashboard, per docs/12-frontend-plan.md §4.2 — composed entirely from
 * the report endpoints, which aggregate in SQL across every account the caller
 * owns (BR-43). Nothing on this page is computed in the browser.
 */
export default function DashboardPage() {
  const { user } = useAuth();
  const router = useRouter();

  // Stable for the lifetime of the page. Recomputing per render would give the
  // fetchers a new identity every time and re-fetch forever.
  const range = useMemo(() => thisMonthRange(), []);

  /*
   * ONE summary call serves both the "what do I have" tiles and the "what
   * happened this month" tiles, because the endpoint answers both at once:
   * totalBalance and accountCount ignore from/to, while credits/debits/net
   * respect them (contract §7). A second unfiltered call would return an
   * identical totalBalance — the asymmetry is the feature, not a bug to work
   * around.
   */
  const fetchSummary = useCallback(
    (signal: AbortSignal) => api.reports.summary(range, signal),
    [range],
  );
  const [summary, reloadSummary] = useResource<SummaryReportResponse>(fetchSummary);

  const fetchAccounts = useCallback((signal: AbortSignal) => api.accounts.list(signal), []);
  const [accounts] = useResource<AccountListResponse>(fetchAccounts);

  const fetchCategories = useCallback(
    (signal: AbortSignal) => api.reports.categories(range, signal),
    [range],
  );
  const [categories] = useResource<CategoryReportResponse>(fetchCategories);

  const fetchMonthly = useCallback((signal: AbortSignal) => api.reports.monthly({}, signal), []);
  const [monthly] = useResource<MonthlyReportResponse>(fetchMonthly);

  /*
   * The current and previous calendar months out of the trailing-12 window,
   * matched by year+month rather than by position: a month with no activity
   * produces no row at all (GROUP BY yields no group for an empty set), so
   * "the last two entries" would silently compare August with May.
   */
  const [current, previous] = useMemo(() => {
    const months = monthly.data?.months ?? [];
    const now = new Date();
    const year = now.getUTCFullYear();
    const month = now.getUTCMonth() + 1;
    const previousDate = new Date(Date.UTC(year, now.getUTCMonth() - 1, 1));

    const find = (y: number, m: number) =>
      months.find((entry) => entry.year === y && entry.month === m);

    return [
      find(year, month),
      find(previousDate.getUTCFullYear(), previousDate.getUTCMonth() + 1),
    ];
  }, [monthly.data]);

  const hasAccounts = (accounts.data?.accounts.length ?? 0) > 0;

  return (
    <>
      <PageHeader
        title={`Welcome back, ${user?.displayName ?? 'there'}`}
        subtitle="Everything below is computed by the database across all of your accounts."
      />

      {summary.status === 'error' && (
        <div className="mb-6">
          <ErrorNotice
            message={messageFor(summary.error)}
            traceId={summary.error instanceof ApiError ? summary.error.traceId : undefined}
            onRetry={reloadSummary}
          />
        </div>
      )}

      {/* A brand-new user gets guidance, not a wall of zeros and empty charts. */}
      {accounts.data && !hasAccounts ? (
        <EmptyState
          title="Let's set up your ledger"
          body="Start with an account — cash in your pocket, a bank account, a wallet. Then record what moves in and out of it, and this dashboard fills itself in."
          action={<Button onClick={() => router.push('/accounts')}>Create your first account</Button>}
        />
      ) : (
        <>
          <div className="mb-6 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            {summary.status === 'loading' ? (
              <>
                <StatCardSkeleton />
                <StatCardSkeleton />
                <StatCardSkeleton />
                <StatCardSkeleton />
              </>
            ) : summary.data ? (
              <>
                <StatCard
                  label="Total balance"
                  sublabel={`Across ${summary.data.accountCount} account${
                    summary.data.accountCount === 1 ? '' : 's'
                  }`}
                  value={formatMoney(summary.data.totalBalance)}
                />
                <StatCard
                  label="Money in"
                  sublabel="This month"
                  value={formatMoney(summary.data.totalCredits)}
                  tone="credit"
                  badge={
                    current && previous ? (
                      <DeltaBadge current={current.credits} previous={previous.credits} />
                    ) : undefined
                  }
                />
                <StatCard
                  label="Money out"
                  sublabel="This month"
                  value={formatMoney(summary.data.totalDebits)}
                  tone="debit"
                  badge={
                    current && previous ? (
                      // Spending MORE is not an improvement, so the colour is
                      // inverted against the income card above.
                      <DeltaBadge
                        current={current.debits}
                        previous={previous.debits}
                        goodWhenUp={false}
                      />
                    ) : undefined
                  }
                />
                <StatCard
                  label="Net"
                  sublabel="This month"
                  value={formatMoney(summary.data.netChange)}
                  tone={summary.data.netChange >= 0 ? 'credit' : 'debit'}
                />
              </>
            ) : null}
          </div>

          <div className="mb-6 grid gap-6 lg:grid-cols-2">
            <Card className="px-5 py-5">
              <h2 className="text-sm font-medium text-ink">Spending by category</h2>
              <p className="mb-4 text-xs text-ink-faint">This month</p>

              {categories.status === 'loading' && <ChartSkeleton rows={4} />}

              {categories.status === 'error' && (
                <ErrorNotice message={messageFor(categories.error)} />
              )}

              {categories.data && categories.data.categories.length === 0 && (
                <p className="py-8 text-center text-sm text-ink-faint">
                  No spending recorded this month.
                </p>
              )}

              {categories.data && categories.data.categories.length > 0 && (
                <>
                  <CategoryBars items={categories.data.categories} />
                  <p className="mt-4 text-xs text-ink-faint">
                    Spending only — transfers and reversed transactions are excluded.
                  </p>
                </>
              )}
            </Card>

            <Card className="px-5 py-5">
              <h2 className="text-sm font-medium text-ink">Income vs expenses</h2>
              <p className="mb-4 text-xs text-ink-faint">Last 12 months</p>

              {monthly.status === 'loading' && <ChartSkeleton rows={4} />}

              {monthly.status === 'error' && <ErrorNotice message={messageFor(monthly.error)} />}

              {monthly.data && monthly.data.months.length === 0 && (
                <p className="py-8 text-center text-sm text-ink-faint">No activity yet.</p>
              )}

              {monthly.data && monthly.data.months.length > 0 && (
                <MonthlyBars months={monthly.data.months} />
              )}
            </Card>
          </div>

          <div className="grid gap-6 lg:grid-cols-2">
            <Card className="px-5 py-5">
              <h2 className="mb-4 text-sm font-medium text-ink">Accounts</h2>

              {accounts.status === 'loading' && (
                <div className="space-y-3">
                  <Skeleton className="h-10 w-full" />
                  <Skeleton className="h-10 w-full" />
                </div>
              )}

              {accounts.data && (
                <ul className="space-y-1">
                  {accounts.data.accounts.map((account) => (
                    <li key={account.id}>
                      <Link
                        href={`/accounts/${account.id}`}
                        className="flex items-center justify-between rounded-lg px-2 py-2 hover:bg-canvas"
                      >
                        <span className="text-sm font-medium text-ink">{account.name}</span>
                        <span className="tabular text-sm text-ink-soft">
                          {formatMoney(account.balance)}
                        </span>
                      </Link>
                    </li>
                  ))}
                </ul>
              )}
            </Card>

            <Card className="px-5 py-5">
              <h2 className="mb-4 text-sm font-medium text-ink">Quick actions</h2>
              <ul className="space-y-1">
                {[
                  { href: '/transactions', label: 'Record a transaction' },
                  { href: '/transfers', label: 'Move money between accounts' },
                  { href: '/reports', label: 'View full reports' },
                ].map((action) => (
                  <li key={action.href}>
                    <Link
                      href={action.href}
                      className="flex items-center justify-between rounded-lg px-2 py-2 text-sm text-ink hover:bg-canvas"
                    >
                      {action.label}
                      <span aria-hidden="true" className="text-ink-faint">
                        →
                      </span>
                    </Link>
                  </li>
                ))}
              </ul>
            </Card>
          </div>
        </>
      )}
    </>
  );
}
