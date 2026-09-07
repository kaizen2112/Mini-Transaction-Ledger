'use client';

import { useState, type FormEvent } from 'react';
import { Button } from '@/components/ui/Button';
import { Drawer } from '@/components/ui/Drawer';
import { Input, Select } from '@/components/ui/Field';
import { ErrorNotice } from '@/components/ui/Feedback';
import { api } from '@/lib/api';
import { ApiError, ErrorCode, fieldErrors, messageFor } from '@/lib/errors';
import { useIdempotencyKey } from '@/lib/useIdempotencyKey';
import type { AccountResponse } from '@/types/api';

/**
 * Create-transfer drawer, per docs/12-frontend-plan.md §4.5. Same
 * idempotency-key lifecycle as CreateTransactionDrawer (§5.3) — a transfer is
 * the clearest case for it, since a retried request would otherwise move the
 * money a second time.
 */
export function CreateTransferDrawer({
  open,
  accounts,
  onClose,
  onCreated,
}: {
  open: boolean;
  accounts: AccountResponse[];
  onClose: () => void;
  onCreated: () => void;
}) {
  const [sourceAccountId, setSourceAccountId] = useState(accounts[0]?.id ?? '');
  const [destinationAccountId, setDestinationAccountId] = useState(accounts[1]?.id ?? accounts[0]?.id ?? '');
  const [amount, setAmount] = useState('');
  const [description, setDescription] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [submitting, setSubmitting] = useState(false);

  const [idempotencyKey, regenerateKey] = useIdempotencyKey(open);

  // Picking a source that is already the destination would leave the two
  // selects agreeing while the destination list no longer offers that option —
  // the control would show one account and submit another. Corrected during
  // render, same pattern as useIdempotencyKey. The backend still owns the rule
  // (400 SAME_ACCOUNT_TRANSFER, handled below): this only stops the UI from
  // presenting a state it would reject.
  const collides = sourceAccountId !== '' && sourceAccountId === destinationAccountId;
  const firstOther = accounts.find((account) => account.id !== sourceAccountId)?.id ?? '';
  if (collides && firstOther !== '') {
    setDestinationAccountId(firstOther);
  }

  function close() {
    setSourceAccountId(accounts[0]?.id ?? '');
    setDestinationAccountId(accounts[1]?.id ?? accounts[0]?.id ?? '');
    setAmount('');
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
      await api.transfers.create(
        {
          sourceAccountId,
          destinationAccountId,
          amount: Number(amount),
          description: description.trim() || undefined,
        },
        idempotencyKey,
      );

      // 201 and 200-replay get identical handling, same reasoning as the
      // transaction drawer (§5.3): a replay means the first attempt already
      // moved the money, so there is nothing new to announce.
      onCreated();
      close();
    } catch (caught) {
      if (caught instanceof ApiError && caught.is(ErrorCode.IdempotencyKeyReused)) {
        regenerateKey();
      }
      setError(caught);
      setSubmitting(false);
    }
  }

  const fields = fieldErrors(error);
  // The client already excludes the source from the destination list below,
  // but the backend still owns the rule (BR-something same-account transfer)
  // — a UI guard is a convenience, never the enforcement, so this still
  // surfaces the server's own rejection if the two ever do match.
  const sameAccount = error instanceof ApiError && error.is(ErrorCode.SameAccountTransfer);
  const showBanner = error != null && Object.keys(fields).length === 0 && !sameAccount;

  return (
    <Drawer open={open} title="New transfer" onClose={close}>
      <form onSubmit={onSubmit} className="flex h-full flex-col" noValidate>
        <div className="flex-1 space-y-4">
          <Select
            label="From account"
            value={sourceAccountId}
            onChange={(e) => setSourceAccountId(e.target.value)}
            error={fields.sourceaccountid}
          >
            {accounts.map((account) => (
              <option key={account.id} value={account.id}>
                {account.name}
              </option>
            ))}
          </Select>

          <Select
            label="To account"
            value={destinationAccountId}
            onChange={(e) => setDestinationAccountId(e.target.value)}
            error={sameAccount ? messageFor(error) : fields.destinationaccountid}
          >
            {accounts
              .filter((account) => account.id !== sourceAccountId)
              .map((account) => (
                <option key={account.id} value={account.id}>
                  {account.name}
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

          <Input
            label="Description"
            maxLength={500}
            placeholder="Moving savings"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            error={fields.description}
          />

          {showBanner && (
            <ErrorNotice message={messageFor(error)} traceId={error instanceof ApiError ? error.traceId : undefined} />
          )}
        </div>

        <div className="mt-6 space-y-2">
          <Button type="submit" loading={submitting} fullWidth disabled={accounts.length < 2}>
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
