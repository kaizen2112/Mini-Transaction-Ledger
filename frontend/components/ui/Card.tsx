import type { ReactNode } from 'react';

export function Card({
  children,
  className = '',
}: {
  children: ReactNode;
  className?: string;
}) {
  return (
    <div className={`rounded-xl border border-line bg-surface ${className}`}>{children}</div>
  );
}

/**
 * One of the summary cards along the top of a page.
 *
 * `value` is always a string the caller has already formatted, so this
 * component never touches a number — formatting lives in lib/format.ts and
 * arithmetic lives in SQL.
 */
export function StatCard({
  label,
  sublabel,
  value,
  tone = 'neutral',
  badge,
}: {
  label: string;
  sublabel?: string;
  value: string;
  tone?: 'neutral' | 'credit' | 'debit';
  badge?: ReactNode;
}) {
  const valueTone =
    tone === 'credit' ? 'text-credit' : tone === 'debit' ? 'text-debit' : 'text-ink';

  return (
    <Card className="px-5 py-4">
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-sm font-medium text-ink">{label}</p>
          {sublabel && <p className="text-xs text-ink-faint">{sublabel}</p>}
        </div>
        {badge}
      </div>
      <p className={`tabular mt-3 text-2xl font-semibold ${valueTone}`}>{value}</p>
    </Card>
  );
}
