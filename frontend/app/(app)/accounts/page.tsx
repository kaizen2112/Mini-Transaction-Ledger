'use client';

import { useCallback, useMemo, useState } from 'react';
import Link from 'next/link';
import { PageHeader } from '@/components/AppShell';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card, StatCard } from '@/components/ui/Card';
import { EmptyState, ErrorNotice, LoadingState } from '@/components/ui/Feedback';
import { CreateAccountDrawer } from '@/components/accounts/CreateAccountDrawer';
import { api } from '@/lib/api';
import { ApiError, messageFor } from '@/lib/errors';
import { formatDate, formatMoney } from '@/lib/format';
import { useResource } from '@/lib/useResource';
import type { AccountListResponse } from '@/types/api';

export default function AccountsPage() {
  const fetchAccounts = useCallback(
    (signal: AbortSignal) => api.accounts.list(signal),
    [],
  );

  const [accounts, reload] = useResource<AccountListResponse>(fetchAccounts);
  const [search, setSearch] = useState('');
  const [drawerOpen, setDrawerOpen] = useState(false);

  /*
   * Filtered in the browser, and that is correct HERE specifically because
   * GET /api/accounts is not paginated (contract §4) — this is the complete
   * set, not a page of it. The same shortcut on transaction history would be a
   * BR-40 violation, which is why history filters go to the server instead.
   */
  const visible = useMemo(() => {
    const list = accounts.data?.accounts ?? [];
    const term = search.trim().toLowerCase();
    return term ? list.filter((account) => account.name.toLowerCase().includes(term)) : list;
  }, [accounts.data, search]);

  return (
    <>
      <PageHeader
        title="Accounts"
        subtitle="See your balances per account and track where money actually sits."
        action={<Button onClick={() => setDrawerOpen(true)}>+ Add account</Button>}
      />

      {accounts.status === 'error' && (
        <div className="mb-6">
          <ErrorNotice
            message={messageFor(accounts.error)}
            traceId={accounts.error instanceof ApiError ? accounts.error.traceId : undefined}
            onRetry={reload}
          />
        </div>
      )}

      {accounts.status === 'loading' && <LoadingState label="Loading accounts…" />}

      {accounts.data && (
        <>
          <div className="mb-6 grid gap-4 sm:grid-cols-2">
            <StatCard
              label="Total balance"
              sublabel="Across all accounts"
              value={formatMoney(accounts.data.totalBalance)}
            />
            <StatCard
              label="Accounts"
              sublabel="Open"
              value={String(accounts.data.accounts.length)}
            />
          </div>

          {accounts.data.accounts.length === 0 ? (
            <EmptyState
              title="No accounts yet"
              body="An account is a place money sits — cash in your pocket, a bank account, a wallet. Create one to start recording transactions."
              action={<Button onClick={() => setDrawerOpen(true)}>+ Add account</Button>}
            />
          ) : (
            <Card>
              <div className="border-b border-line px-4 py-3">
                <input
                  type="search"
                  value={search}
                  onChange={(event) => setSearch(event.target.value)}
                  placeholder="Search account"
                  aria-label="Search account"
                  className="w-full max-w-xs rounded-lg border border-line-strong bg-surface px-3 py-1.5 text-sm placeholder:text-ink-faint focus:outline-2 focus:outline-brand"
                />
              </div>

              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="border-b border-line text-left text-xs uppercase tracking-wide text-ink-faint">
                      <th className="px-4 py-3 font-medium">Name</th>
                      <th className="px-4 py-3 font-medium">Type</th>
                      <th className="px-4 py-3 font-medium">Opened</th>
                      <th className="px-4 py-3 text-right font-medium">Balance</th>
                    </tr>
                  </thead>
                  <tbody>
                    {visible.map((account) => (
                      <tr key={account.id} className="border-b border-line last:border-0">
                        <td className="px-4 py-3">
                          <Link
                            href={`/accounts/${account.id}`}
                            className="font-medium text-ink underline-offset-2 hover:underline"
                          >
                            {account.name}
                          </Link>
                        </td>
                        <td className="px-4 py-3">
                          <Badge>{account.type}</Badge>
                        </td>
                        <td className="px-4 py-3 text-ink-soft">{formatDate(account.createdAt)}</td>
                        <td className="tabular px-4 py-3 text-right font-medium text-ink">
                          {formatMoney(account.balance)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {visible.length === 0 && (
                <p className="px-4 py-8 text-center text-sm text-ink-faint">
                  No account matches “{search}”.
                </p>
              )}
            </Card>
          )}
        </>
      )}

      <CreateAccountDrawer
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
        onCreated={reload}
      />
    </>
  );
}
