import { formatMoney, formatMonth, formatMonthShort } from '@/lib/format';
import type { MonthlyReportItem } from '@/types/api';

/**
 * Grouped vertical bars — credits beside debits, one pair per month, per
 * docs/12-frontend-plan.md §3.4. Hand-rolled for the same reason as
 * CategoryBars.
 *
 * Both series share one scale (the largest single value across BOTH), because
 * the entire point of putting them side by side is comparing their heights. A
 * per-series scale would draw a 200 debit the same height as a 20,000 credit
 * and make the chart actively misleading.
 */
export function MonthlyBars({ months }: { months: MonthlyReportItem[] }) {
  const largest = Math.max(...months.flatMap((month) => [month.credits, month.debits]), 0);

  return (
    <div>
      <div className="mb-3 flex items-center gap-4 text-xs text-ink-soft">
        <span className="flex items-center gap-1.5">
          <span className="size-2.5 rounded-sm bg-credit" aria-hidden="true" />
          Credits
        </span>
        <span className="flex items-center gap-1.5">
          <span className="size-2.5 rounded-sm bg-debit" aria-hidden="true" />
          Debits
        </span>
      </div>

      <div className="flex items-end gap-2 overflow-x-auto pb-1" style={{ height: 180 }}>
        {months.map((month) => {
          const creditHeight = largest > 0 ? (month.credits / largest) * 100 : 0;
          const debitHeight = largest > 0 ? (month.debits / largest) * 100 : 0;
          const label = formatMonth(month.year, month.month);

          return (
            <div key={`${month.year}-${month.month}`} className="flex min-w-10 flex-1 flex-col items-center gap-1">
              <div className="flex h-full w-full items-end justify-center gap-0.5">
                {/*
                  title on each bar so the exact figure is one hover away —
                  the axis is deliberately unlabelled, since a reader who
                  wants precise numbers has the monthly table in /reports.
                */}
                <div
                  className="w-1/2 rounded-t-sm bg-credit"
                  style={{ height: `${creditHeight}%` }}
                  title={`${label} credits ${formatMoney(month.credits)}`}
                />
                <div
                  className="w-1/2 rounded-t-sm bg-debit"
                  style={{ height: `${debitHeight}%` }}
                  title={`${label} debits ${formatMoney(month.debits)}`}
                />
              </div>
              <span className="whitespace-nowrap text-[11px] text-ink-faint">
                {formatMonthShort(month.year, month.month)}
              </span>
            </div>
          );
        })}
      </div>
    </div>
  );
}
