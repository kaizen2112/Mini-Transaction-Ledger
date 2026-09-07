/**
 * Loading placeholders that hold the same space the real content will take.
 *
 * Used instead of a centred spinner on the dashboard and reports, where
 * several panels load independently: a spinner per panel makes the page
 * flicker and jump as each one resolves, while a skeleton keeps the layout
 * still and only swaps in the numbers.
 */
export function Skeleton({ className = '' }: { className?: string }) {
  return <div className={`animate-pulse rounded bg-line ${className}`} aria-hidden="true" />;
}

/** Placeholder shaped like a StatCard, for the summary rows. */
export function StatCardSkeleton() {
  return (
    <div className="rounded-xl border border-line bg-surface px-5 py-4">
      <Skeleton className="h-4 w-24" />
      <Skeleton className="mt-2 h-3 w-16" />
      <Skeleton className="mt-3 h-7 w-32" />
    </div>
  );
}

/** Placeholder shaped like a bar chart, for either chart panel. */
export function ChartSkeleton({ rows = 5 }: { rows?: number }) {
  return (
    <div className="space-y-3 px-5 py-5">
      {Array.from({ length: rows }, (_, index) => (
        <div key={index} className="flex items-center gap-3">
          <Skeleton className="h-3 w-24" />
          <Skeleton className="h-3 flex-1" />
        </div>
      ))}
    </div>
  );
}
