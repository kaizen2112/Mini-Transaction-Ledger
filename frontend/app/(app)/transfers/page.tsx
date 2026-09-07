'use client';

import { useCallback, useMemo, useState } from 'react';
import { PageHeader } from '@/components/AppShell';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { EmptyState, ErrorNotice, LoadingState } from '@/components/ui/Feedback';
import { Pagination } from '@/components/ui/Pagination';
import { CreateTransferDrawer } from '@/components/transfers/CreateTransferDrawer';
import { api } from '@/lib/api';
import { ApiError, messageFor } from '@/lib/errors';
import { formatDateTime, formatMoney } from '@/lib/format';
import { DEFAULT_PAGE_SIZE, type PageSize } from '@/lib/query';
import { useResource } from '@/lib/useResource';
import type { AccountListResponse, PagedResponse, TransferResponse } from '@/types/api';

/**
 * /transfers per docs/12-frontend-plan.md §4.5. No filters here — the
 * contract (§6) gives GET /api/transfers pagination only, and a bound-but-
 * ignored filter would be worse than no filter: the caller would believe it
 * had filtered.
 */
export default function TransfersPage() {
  const fetchAccounts = useCallback((signal: AbortSignal) => api.accounts.list(signal), []);
  const [accounts, reloadAccounts] = useResource<AccountListResponse>(fetchAccounts);

  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState<PageSize>(DEFAULT_PAGE_SIZE);
  const [drawerOpen, setDrawerOpen] = useState(false);

  const fetchTransfers = useCallback(
    (signal: AbortSignal) => api.transfers.list({ page, pageSize }, signal),
    [page, pageSize],
  );
  const [transfers, reload] = useResource<PagedResponse<TransferResponse>>(fetchTransfers);

  // TransferResponse carries account ids, not names (contract §6) — resolved
  // here from the account list already loaded for the create drawer, rather
  // than a second lookup per row.
  const accountName = useMemo(() => {
    const byId = new Map(accounts.data?.accounts.map((account) => [account.id, account.name]));
    return (id: string) => byId.get(id) ?? 'Unknown account';
  }, [accounts.data]);

  const canTransfer = (accounts.data?.accounts.length ?? 0) >= 2;

  if (accounts.status === 'loading') {
    return <LoadingState label="Loading accounts…" />;
  }

  if (accounts.status === 'error' && !accounts.data) {
    return (
      <>
        <PageHeader title="Transfers" subtitle="Move money between your own accounts." />
        <ErrorNotice
          message={messageFor(accounts.error)}
          traceId={accounts.error instanceof ApiError ? accounts.error.traceId : undefined}
          onRetry={reloadAccounts}
        />
      </>
    );
  }

  return (
    <>
      <PageHeader
        title="Transfers"
        subtitle="Move money between your own accounts."
        action={
          <Button onClick={() => setDrawerOpen(true)} disabled={!canTransfer}>
            + New transfer
          </Button>
        }
      />

      {!canTransfer && (
        <p className="mb-4 text-sm text-ink-faint">
          A transfer needs two accounts. Add a second account to use this page.
        </p>
      )}

      {transfers.status === 'error' && (
        <div className="mb-4">
          <ErrorNotice
            message={messageFor(transfers.error)}
            traceId={transfers.error instanceof ApiError ? transfers.error.traceId : undefined}
            onRetry={reload}
          />
        </div>
      )}

      {transfers.status === 'loading' && <LoadingState label="Loading transfers…" />}

      {transfers.data && transfers.data.items.length === 0 && (
        <EmptyState
          title="No transfers yet"
          body="A transfer moves money between two of your own accounts in one atomic step — both balances change together, or neither does."
          action={
            canTransfer ? <Button onClick={() => setDrawerOpen(true)}>+ New transfer</Button> : undefined
          }
        />
      )}

      {transfers.data && transfers.data.items.length > 0 && (
        <Card>
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-line text-left text-xs uppercase tracking-wide text-ink-faint">
                  <th className="px-4 py-3 font-medium">When</th>
                  <th className="px-4 py-3 font-medium">From</th>
                  <th className="px-4 py-3 font-medium">To</th>
                  <th className="px-4 py-3 text-right font-medium">Amount</th>
                </tr>
              </thead>
              <tbody>
                {transfers.data.items.map((transfer) => (
                  <tr key={transfer.id} className="border-b border-line last:border-0">
                    <td className="whitespace-nowrap px-4 py-3 text-ink-soft">
                      {formatDateTime(transfer.occurredAt)}
                    </td>
                    <td className="px-4 py-3 text-ink">{accountName(transfer.sourceAccountId)}</td>
                    <td className="px-4 py-3 text-ink">
                      <span className="inline-flex items-center gap-1.5">
                        <Badge tone="brand">Transfer</Badge>
                        {accountName(transfer.destinationAccountId)}
                      </span>
                    </td>
                    <td className="tabular px-4 py-3 text-right font-medium text-ink">
                      {formatMoney(transfer.amount)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <Pagination
            page={transfers.data.page}
            pageSize={transfers.data.pageSize}
            totalItems={transfers.data.totalItems}
            totalPages={transfers.data.totalPages}
            onPageChange={setPage}
            onPageSizeChange={(size) => {
              setPageSize(size);
              setPage(1);
            }}
          />
        </Card>
      )}

      <CreateTransferDrawer
        open={drawerOpen}
        accounts={accounts.data?.accounts ?? []}
        onClose={() => setDrawerOpen(false)}
        onCreated={reload}
      />
    </>
  );
}
