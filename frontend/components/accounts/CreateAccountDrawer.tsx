'use client';

import { useState, type FormEvent } from 'react';
import { Button } from '@/components/ui/Button';
import { Drawer } from '@/components/ui/Drawer';
import { Input, Select } from '@/components/ui/Field';
import { ErrorNotice } from '@/components/ui/Feedback';
import { api } from '@/lib/api';
import { ApiError, ErrorCode, fieldErrors, messageFor, traceIdFor } from '@/lib/errors';
import { ACCOUNT_TYPES, type AccountType } from '@/types/api';

/**
 * Name and type only, per docs/12-frontend-plan.md §4.3.
 *
 * The reference design also offered Bank/Provider, Status, Starting Balance and
 * an as-of date. None of them exist on the entity, and BR-15 makes the third
 * one actively wrong: every account starts at 0.00 and is funded by recording a
 * Credit. Four fields the API would have silently ignored.
 *
 * No Idempotency-Key here either (BR-32): the unique index on (UserId, Name)
 * already makes a double submission return 409 ACCOUNT_NAME_TAKEN rather than
 * creating two accounts.
 */
export function CreateAccountDrawer({
  open,
  onClose,
  onCreated,
}: {
  open: boolean;
  onClose: () => void;
  onCreated: () => void;
}) {
  const [name, setName] = useState('');
  const [type, setType] = useState<AccountType>('Cash');
  const [error, setError] = useState<unknown>(null);
  const [submitting, setSubmitting] = useState(false);

  function close() {
    setName('');
    setType('Cash');
    setError(null);
    setSubmitting(false);
    onClose();
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    setSubmitting(true);
    setError(null);

    try {
      await api.accounts.create({ name: name.trim(), type });
      onCreated();
      close();
    } catch (caught) {
      setError(caught);
      setSubmitting(false);
    }
  }

  const fields = fieldErrors(error);
  const nameTaken = error instanceof ApiError && error.is(ErrorCode.AccountNameTaken);

  return (
    <Drawer open={open} title="Add account" onClose={close}>
      <form onSubmit={onSubmit} className="flex h-full flex-col" noValidate>
        <div className="flex-1 space-y-4">
          <Input
            label="Name"
            required
            maxLength={100}
            placeholder="Cash"
            value={name}
            onChange={(e) => setName(e.target.value)}
            // A 409 is about the name, so it renders on the name field rather
            // than as a general banner.
            error={nameTaken ? messageFor(error) : fields.name}
          />

          <Select
            label="Type"
            value={type}
            onChange={(e) => setType(e.target.value as AccountType)}
            error={fields.type}
          >
            {ACCOUNT_TYPES.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </Select>

          <p className="rounded-lg border border-line bg-canvas px-3 py-2 text-xs text-ink-soft">
            New accounts start at {' '}
            <span className="tabular font-medium text-ink">BDT 0.00</span>. Add money by
            recording a credit.
          </p>

          {error != null && !nameTaken && Object.keys(fields).length === 0 && (
            <ErrorNotice
              message={messageFor(error)}
              traceId={traceIdFor(error)}
            />
          )}
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
