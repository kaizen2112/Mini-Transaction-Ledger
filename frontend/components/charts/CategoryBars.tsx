import { formatMoney } from '@/lib/format';
import type { CategoryReportItem } from '@/types/api';

/**
 * Ranked horizontal bars, per docs/12-frontend-plan.md §3.4. No chart library:
 * this is ~40 lines of flexbox, and a dependency for one static chart is more
 * code to explain, not less.
 *
 * A donut was rejected for this data. The question is "where did my money go,
 * most first", and ranked bars answer it directly — a reader compares lengths
 * on a shared baseline instead of arc angles.
 *
 * The rows arrive already ordered biggest-first from SQL (BR-43), so this
 * component renders them in the order given and never re-sorts. Sorting here
 * would be a second, competing ordering that could silently disagree with the
 * API's.
 */
export function CategoryBars({ items }: { items: CategoryReportItem[] }) {
  // Scaled against the largest bar rather than the total, so the biggest
  // category always fills the track and small ones stay visible. Guarded
  // against zero because every amount could legitimately be 0.00.
  const largest = Math.max(...items.map((item) => item.totalAmount), 0);

  return (
    <ul className="space-y-3">
      {items.map((item) => {
        const width = largest > 0 ? (item.totalAmount / largest) * 100 : 0;

        return (
          <li key={item.category}>
            <div className="flex items-baseline justify-between gap-3 text-sm">
              <span className="font-medium text-ink">{item.category}</span>
              <span className="tabular text-ink-soft">
                {formatMoney(item.totalAmount)}
                <span className="ml-2 text-xs text-ink-faint">
                  {item.transactionCount === 1 ? '1 txn' : `${item.transactionCount} txns`}
                </span>
              </span>
            </div>
            <div className="mt-1.5 h-2 w-full overflow-hidden rounded-full bg-canvas">
              {/*
                aria-hidden: the bar is a visual restatement of the figure
                already read out on the line above, so announcing it again
                would just be noise to a screen reader.
              */}
              <div
                aria-hidden="true"
                className="h-full rounded-full bg-brand"
                style={{ width: `${width}%` }}
              />
            </div>
          </li>
        );
      })}
    </ul>
  );
}
