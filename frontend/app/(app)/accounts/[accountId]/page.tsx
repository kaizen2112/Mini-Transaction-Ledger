'use client';

import { useCallback, useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { PageHeader } from '@/components/AppShell';
import { Badge } from '@/components/ui/Badge';
import { Card, StatCard } from '@/components/ui/Card';
import { ErrorNotice, LoadingState } from '@/components/ui/Feedback';
import { ReverseTransactionDialog } from '@/components/transactions/ReverseTransactionDialog';
import { TransactionTable } from '@/components/transactions/TransactionTable';
import { api } from '@/lib/api';
import { ApiError, ErrorCode, messageFor } from '@/lib/errors';
import { formatDateTime, formatMoney } from '@/lib/format';
import { useResource } from '@/lib/useResource';
import type { AccountBalanceResponse, AccountResponse, PagedResponse, TransactionResponse } from '@/types/api';

/** The most recent activity shown here. The full filterable history — every
 * BR-42 filter, real pagination — lives at /transactions, which this page
 * links to rather than duplicating (§3.1/§4.3). */
const RECENT_COUNT = 10;

interface AccountDetail {
  account: AccountResponse;
  balance: AccountBalanceResponse;
}

export default function AccountDetailPage() {
  const params = useParams<{ accountId: string }>();
  const accountId = params.accountId;
  const router = useRouter();

  const [reversing, setReversing] = useState<TransactionResponse | null>(null);

  const fetchDetail = useCallback(
    async (signal: AbortSignal): Promise<AccountDetail> => {
      // Two endpoints on purpose. /balance exists so a balance-only poll does
      // not transfer the whole account object (contract §4); fetching both here
      // exercises it and gives the `asOf` stamp.
      const [account, balance] = await Promise.all([
        api.accounts.get(accountId, signal),
        api.accounts.balance(accountId, signal),
      ]);
      return { account, balance };
    },
    [accountId],
  );

  const [detail, reload] = useResource<AccountDetail>(fetchDetail);

  const fetchRecent = useCallback(
    (signal: AbortSignal) =>
      api.transactions.list(accountId, { page: 1, pageSize: RECENT_COUNT }, signal),
    [accountId],
  );
  const [recent, reloadRecent] = useResource<PagedResponse<TransactionResponse>>(fetchRecent);

  if (detail.status === 'loading') return <LoadingState label="Loading account…" />;

  // BR-08: an account that is not yours returns 404, exactly as one that does
  // not exist does. The UI says "not found" for both and never hints that the
  // distinction exists — saying "no access" here would leak precisely what the
  // backend refused to confirm.
  if (detail.status === 'error' && detail.error instanceof ApiError && detail.error.is(ErrorCode.NotFound)) {
    return (
      <>
        <PageHeader title="Not found" subtitle="This account does not exist." />
        <Link
          href="/accounts"
          className="text-sm font-medium text-brand underline underline-offset-2"
        >
          Back to accounts
        </Link>
      </>
    );
  }

  if (detail.status === 'error' && !detail.data) {
    return (
      <ErrorNotice
        message={messageFor(detail.error)}
        traceId={detail.error instanceof ApiError ? detail.error.traceId : undefined}
        onRetry={reload}
      />
    );
  }

  if (!detail.data) return null;

  const { account, balance } = detail.data;

  function reloadAll() {
    reload();
    reloadRecent();
  }

  return (
    <>
      <nav className="mb-4 text-sm text-ink-faint">
        <Link href="/accounts" className="hover:text-ink">
          Accounts
        </Link>
        <span className="px-2">/</span>
        <span className="text-ink">{account.name}</span>
      </nav>

      <PageHeader
        title={account.name}
        subtitle="Balance now, and this account's most recent activity."
        action={<Badge>{account.type}</Badge>}
      />

      {detail.status === 'error' && (
        <div className="mb-6">
          <ErrorNotice message={messageFor(detail.error)} onRetry={reload} />
        </div>
      )}

      <div className="mb-6 grid gap-4 sm:grid-cols-2">
        <StatCard
          label="Current balance"
          sublabel={`As of ${formatDateTime(balance.asOf)}`}
          value={formatMoney(balance.balance)}
        />
        <StatCard
          label="Opened"
          sublabel="Account created"
          value={formatDateTime(account.createdAt)}
        />
      </div>

      <Card>
        <div className="flex items-center justify-between border-b border-line px-4 py-3">
          <h2 className="text-sm font-medium text-ink">Recent activity</h2>
          <button
            type="button"
            onClick={() => router.push(`/transactions?accountId=${accountId}`)}
            className="text-sm font-medium text-brand underline-offset-2 hover:underline"
          >
            View all transactions →
          </button>
        </div>

        {recent.status === 'loading' && <LoadingState label="Loading activity…" />}

        {recent.status === 'error' && (
          <div className="px-4 py-4">
            <ErrorNotice message={messageFor(recent.error)} onRetry={reloadRecent} />
          </div>
        )}

        {recent.data && recent.data.items.length === 0 && (
          <p className="px-4 py-10 text-center text-sm text-ink-faint">
            No transactions yet. Record one from the Transactions page.
          </p>
        )}

        {recent.data && recent.data.items.length > 0 && (
          <TransactionTable items={recent.data.items} onReverse={setReversing} />
        )}
      </Card>

      <ReverseTransactionDialog transaction={reversing} onClose={() => setReversing(null)} onReversed={reloadAll} />
    </>
  );
}
