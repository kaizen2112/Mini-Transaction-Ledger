'use client';

import { useCallback, useEffect, useState } from 'react';

/**
 * One GET, with abort-on-unmount and a manual reload.
 *
 * Extracted for two reasons that are not "it was repeated twice". First, every
 * state update happens inside a promise callback rather than synchronously in
 * the effect body, which is what React 19 wants and what stops a load from
 * cascading an extra render. Second, it aborts: without the AbortController, a
 * user who changes filters quickly can have two requests in flight and the
 * SLOWER one wins, silently painting stale data over fresh.
 *
 * Getting either of those wrong is invisible until it matters, so they are
 * written once here rather than at every call site.
 */
export type Resource<T> =
  | { status: 'loading'; data: null; error: null }
  | { status: 'ready'; data: T; error: null }
  | { status: 'error'; data: T | null; error: unknown };

export function useResource<T>(
  fetcher: (signal: AbortSignal) => Promise<T>,
): [Resource<T>, () => void] {
  const [state, setState] = useState<Resource<T>>({ status: 'loading', data: null, error: null });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    const controller = new AbortController();

    fetcher(controller.signal)
      .then((data) => {
        if (!controller.signal.aborted) setState({ status: 'ready', data, error: null });
      })
      .catch((error: unknown) => {
        // An abort is this component tidying up, not a failure to report.
        if (controller.signal.aborted) return;
        // Keep any data already on screen so a failed refresh does not blank
        // the page — the user can still read what loaded a moment ago.
        setState((previous) => ({ status: 'error', data: previous.data, error }));
      });

    return () => controller.abort();
  }, [fetcher, attempt]);

  const reload = useCallback(() => setAttempt((n) => n + 1), []);

  return [state, reload];
}
