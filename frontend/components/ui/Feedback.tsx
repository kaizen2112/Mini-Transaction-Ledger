import type { ReactNode } from 'react';
import { Spinner } from '@/components/ui/Spinner';

/**
 * An error the user should see, with the traceId when there is one.
 *
 * The traceId is shown deliberately: it is in the problem+json body (contract
 * §1.1) and it is the one thing that makes a bug report actionable. Hiding it
 * to keep the UI tidy costs more than it saves.
 */
export function ErrorNotice({
  message,
  traceId,
  onRetry,
}: {
  message: string;
  traceId?: string;
  onRetry?: () => void;
}) {
  return (
    <div role="alert" className="rounded-lg border border-debit/30 bg-debit-tint px-4 py-3">
      <p className="text-sm text-ink">{message}</p>
      {traceId && (
        <p className="mt-1 font-mono text-[11px] text-ink-faint">trace {traceId}</p>
      )}
      {onRetry && (
        <button
          type="button"
          onClick={onRetry}
          className="mt-2 text-sm font-medium text-brand underline underline-offset-2"
        >
          Try again
        </button>
      )}
    </div>
  );
}

/** A page or panel waiting on its first load. */
export function LoadingState({ label = 'Loading…' }: { label?: string }) {
  return (
    <div className="flex items-center justify-center gap-2 py-16 text-sm text-ink-faint">
      <Spinner className="size-4" />
      <span>{label}</span>
    </div>
  );
}

/**
 * A real empty state, not a zero. A new user's first screen should say what to
 * do next, because a table of zeros looks like something failed to load.
 */
export function EmptyState({
  title,
  body,
  action,
}: {
  title: string;
  body: string;
  action?: ReactNode;
}) {
  return (
    <div className="flex flex-col items-center justify-center rounded-xl border border-dashed border-line-strong bg-surface px-6 py-14 text-center">
      <p className="text-base font-medium text-ink">{title}</p>
      <p className="mt-1 max-w-md text-sm text-ink-soft">{body}</p>
      {action && <div className="mt-4">{action}</div>}
    </div>
  );
}
