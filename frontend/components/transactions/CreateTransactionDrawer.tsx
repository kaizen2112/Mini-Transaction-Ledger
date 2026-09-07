'use client';

import { useState, type FormEvent } from 'react';
import { Button } from '@/components/ui/Button';
import { Drawer } from '@/components/ui/Drawer';
import { Input, Select } from '@/components/ui/Field';
import { ErrorNotice } from '@/components/ui/Feedback';
import { api } from '@/lib/api';
import { ApiError, ErrorCode, fieldErrors, messageFor } from '@/lib/errors';
import { useIdempotencyKey } from '@/lib/useIdempotencyKey';
import { ASSIGNABLE_CATEGORIES, TRANSACTION_TYPES, type TransactionCategory, type TransactionType } from '@/types/api';

/**
 * Add-transaction drawer, per docs/12-frontend-plan.md §4.4 and the
 * idempotency lifecycle in §5.3.
 *
 * No date field: OccurredAt is assigned by the server at insert time, and a
 * field the backend would silently discard is worse than no field. Category
 * is required and restricted to ASSIGNABLE_CATEGORIES — Transfer and Reversal
 * are system-only (BR-24) and would 400 CATEGORY_SYSTEM_ONLY if offered here.
 */
export function CreateTransactionDrawer({
  open,
  accountId,
  onClose,
  onCreated,
}: {
  open: boolean;
  accountId: string;
  onClose: () => void;
  onCreated: () => void;
}) {
  const [type, setType] = useState<TransactionType>('Debit');
  const [amount, setAmount] = useState('');
  const [category, setCategory] = useState<TransactionCategory>(ASSIGNABLE_CATEGORIES[0]);
  const [description, setDescription] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [submitting, setSubmitting] = useState(false);

  // One key per drawer session, reused across retries, regenerated on reopen
  // (§5.3) or explicitly below if the server says it was reused for a
  // different body.
  const [idempotencyKey, regenerateKey] = useIdempotencyKey(open);

  function close() {
    setType('Debit');
    setAmount('');
    setCategory(ASSIGNABLE_CATEGORIES[0]);
    setDescription('');
    setError(null);
    setSubmitting(false);
    onClose();
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    setSubmitting(true);
    setError(null);

    try {
      // 201 (created) and 200 + Idempotent-Replay (this exact submission
      // already succeeded once) get IDENTICAL handling here on purpose: a
      // replay means nothing new happened, and the UI must not report
      // "created" a second time or insert a second row. api.transactions
      // .create resolves to an ApiResult carrying isReplay precisely so a
      // caller COULD distinguish them; this one deliberately does not.
      await api.transactions.create(
        accountId,
        {
          type,
          amount: Number(amount),
          category,
          description: description.trim() || undefined,
        },
        idempotencyKey,
      );

      onCreated();
      close();
    } catch (caught) {
      if (caught instanceof ApiError && caught.is(ErrorCode.IdempotencyKeyReused)) {
        // The same key was sent with a different body than the first time —
        // reusing it again would just repeat this same rejection, so a fresh
        // key is issued and the user can resubmit.
        regenerateKey();
      }
      setError(caught);
      setSubmitting(false);
    }
  }

  const fields = fieldErrors(error);
  const showBanner = error != null && Object.keys(fields).length === 0;

  return (
    <Drawer open={open} title="Add transaction" onClose={close}>
      <form onSubmit={onSubmit} className="flex h-full flex-col" noValidate>
        <div className="flex-1 space-y-4">
          <Select
            label="Type"
            value={type}
            onChange={(e) => setType(e.target.value as TransactionType)}
            error={fields.type}
          >
            {TRANSACTION_TYPES.map((option) => (
              <option key={option} value={option}>
                {option === 'Credit' ? 'Credit (money in)' : 'Debit (money out)'}
              </option>
            ))}
          </Select>

          <Input
            label="Amount"
            type="number"
            inputMode="decimal"
            min="0.01"
            step="0.01"
            required
            placeholder="0.00"
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            error={
              error instanceof ApiError &&
              (error.is(ErrorCode.AmountNotPositive) ||
                error.is(ErrorCode.AmountTooLarge) ||
                error.is(ErrorCode.AmountScaleInvalid))
                ? messageFor(error)
                : fields.amount
            }
          />

          <Select
            label="Category"
            value={category}
            onChange={(e) => setCategory(e.target.value as TransactionCategory)}
            error={fields.category}
          >
            {ASSIGNABLE_CATEGORIES.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </Select>

          <Input
            label="Description"
            maxLength={500}
            placeholder="Groceries"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            error={fields.description}
          />

          {showBanner && <ErrorNotice message={messageFor(error)} traceId={error instanceof ApiError ? error.traceId : undefined} />}
        </div>

        <div className="mt-6 space-y-2">
          <Button type="submit" loading={submitting} fullWidth>
            Save
          </Button>
          <Button type="button" variant="ghost" fullWidth onClick={close}>
            Cancel
          </Button>
        </div>
      </form>
    </Drawer>
  );
}
