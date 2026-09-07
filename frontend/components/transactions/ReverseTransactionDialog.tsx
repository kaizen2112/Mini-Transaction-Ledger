'use client';

import { useState } from 'react';
import { Button } from '@/components/ui/Button';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { ErrorNotice } from '@/components/ui/Feedback';
import { api } from '@/lib/api';
import { messageFor } from '@/lib/errors';
import { formatSignedMoney } from '@/lib/format';
import type { TransactionResponse } from '@/types/api';

/**
 * Reverse per docs/12-frontend-plan.md §4.4: a confirm dialog, not a drawer —
 * there is nothing to fill in except an optional note. No Idempotency-Key
 * here either: the unique index on ReversesTransactionId already makes a
 * repeat impossible (contract §5), so there is no retry-duplication risk this
 * component needs to guard against.
 *
 * `transaction` is null while closed, which is also what tells this component
 * WHICH row to reverse — no separate `open` boolean needed.
 */
export function ReverseTransactionDialog({
  transaction,
  onClose,
  onReversed,
}: {
  transaction: TransactionResponse | null;
  onClose: () => void;
  onReversed: () => void;
}) {
  const [description, setDescription] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [submitting, setSubmitting] = useState(false);

  function close() {
    setDescription('');
    setError(null);
    setSubmitting(false);
    onClose();
  }

  async function confirm() {
    if (!transaction) return;

    setSubmitting(true);
    setError(null);

    try {
      await api.transactions.reverse(transaction.id, {
        description: description.trim() || undefined,
      });
      onReversed();
      close();
    } catch (caught) {
      setError(caught);
      setSubmitting(false);
    }
  }

  return (
    <ConfirmDialog open={transaction !== null} title="Reverse this transaction?" onClose={close}>
      {transaction && (
        <>
          <p className="text-sm text-ink-soft">
            This records a compensating entry of{' '}
            <span className="tabular font-medium text-ink">
              {formatSignedMoney(transaction.amount, transaction.type === 'Credit' ? 'Debit' : 'Credit')}
            </span>
            . The original transaction is never changed — it stays on the ledger, marked
            reversed (BR-21).
          </p>

          <label className="mt-3 block text-sm font-medium text-ink" htmlFor="reverse-note">
            Note (optional)
          </label>
          <input
            id="reverse-note"
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            placeholder="Returned the item"
            className="mt-1.5 w-full rounded-lg border border-line-strong bg-surface px-3 py-2 text-sm placeholder:text-ink-faint focus:outline-2 focus:outline-brand"
          />

          {error != null && (
            <div className="mt-3">
              <ErrorNotice message={messageFor(error)} />
            </div>
          )}

          <div className="mt-4 flex justify-end gap-2">
            <Button variant="ghost" onClick={close}>
              Cancel
            </Button>
            <Button variant="danger" loading={submitting} onClick={confirm}>
              Reverse
            </Button>
          </div>
        </>
      )}
    </ConfirmDialog>
  );
}
