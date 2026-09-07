import { Badge } from '@/components/ui/Badge';
import { formatDateTime, formatSignedMoney } from '@/lib/format';
import type { TransactionResponse } from '@/types/api';

/**
 * The row rendering shared by /transactions (the full filterable history) and
 * the account-detail page's recent-activity panel — same rows, same badges,
 * same reverse action, so the two cannot quietly drift apart.
 *
 * Row actions: Reverse only. There is no Edit and no Delete anywhere in this
 * table, disabled or otherwise — the ledger is append-only (BR-21, BR-22), and
 * "you cannot edit this" is the design, not a limitation to apologise for
 * with a greyed-out button (docs/12-frontend-plan.md §4.4).
 */
export function TransactionTable({
  items,
  onReverse,
}: {
  items: TransactionResponse[];
  onReverse: (transaction: TransactionResponse) => void;
}) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b border-line text-left text-xs uppercase tracking-wide text-ink-faint">
            <th className="px-4 py-3 font-medium">Date</th>
            <th className="px-4 py-3 font-medium">Description</th>
            <th className="px-4 py-3 font-medium">Category</th>
            <th className="px-4 py-3 font-medium">Status</th>
            <th className="px-4 py-3 text-right font-medium">Amount</th>
            <th className="px-4 py-3" />
          </tr>
        </thead>
        <tbody>
          {items.map((transaction) => (
            <TransactionRow key={transaction.id} transaction={transaction} onReverse={onReverse} />
          ))}
        </tbody>
      </table>
    </div>
  );
}

function TransactionRow({
  transaction,
  onReverse,
}: {
  transaction: TransactionResponse;
  onReverse: (transaction: TransactionResponse) => void;
}) {
  // Static column ordering fixed server-side (OccurredAt DESC, Id DESC,
  // BR-41) — headers above are labels, not sort controls (§2.1).
  const canReverse =
    !transaction.isReversed && transaction.transferId === null && transaction.reversesTransactionId === null;

  return (
    <tr className="border-b border-line last:border-0">
      <td className="whitespace-nowrap px-4 py-3 text-ink-soft">
        {formatDateTime(transaction.occurredAt)}
      </td>
      <td className="px-4 py-3 text-ink">{transaction.description || <span className="text-ink-faint">—</span>}</td>
      <td className="px-4 py-3 text-ink-soft">{transaction.category}</td>
      <td className="px-4 py-3">
        <div className="flex flex-wrap gap-1.5">
          {transaction.isReversed && <Badge tone="warn">Reversed</Badge>}
          {transaction.reversesTransactionId !== null && <Badge tone="neutral">Reversal</Badge>}
          {transaction.transferId !== null && <Badge tone="brand">Transfer</Badge>}
        </div>
      </td>
      <td
        className={[
          'tabular px-4 py-3 text-right font-medium',
          transaction.type === 'Credit' ? 'text-credit' : 'text-debit',
          transaction.isReversed ? 'line-through opacity-60' : '',
        ].join(' ')}
      >
        {formatSignedMoney(transaction.amount, transaction.type)}
      </td>
      <td className="px-4 py-3 text-right">
        {canReverse && (
          <button
            type="button"
            onClick={() => onReverse(transaction)}
            className="text-sm font-medium text-brand underline-offset-2 hover:underline"
          >
            Reverse
          </button>
        )}
      </td>
    </tr>
  );
}
