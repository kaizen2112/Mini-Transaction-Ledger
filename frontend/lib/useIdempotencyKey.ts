'use client';

import { useCallback, useState } from 'react';

/**
 * The idempotency-key lifecycle from docs/12-frontend-plan.md §5.3.
 *
 * "The key belongs to the submission, not to the click." One key is generated
 * when a drawer opens, and it survives every retry of that same submission —
 * a double-click, or the user pressing Save again after a timeout — because a
 * fresh one is only issued on the specific transition from closed to open, not
 * on every re-render a submit attempt causes. A fresh key is issued when the
 * drawer closes and reopens for a new entry, or when the server explicitly
 * says the old one was reused for a different body (422
 * IDEMPOTENCY_KEY_REUSED) — the one case where reusing it again would just
 * repeat the same rejection.
 *
 * Regenerating per click is the mistake this exists to prevent: two keys are
 * two transactions, and the whole point of BR-34 becomes decorative.
 *
 * The open-transition check runs during render rather than in a useEffect —
 * React's documented pattern for "adjust state when a prop changes"
 * (react.dev/learn/you-might-not-need-an-effect#adjusting-some-state-when-a-prop-changes).
 * An effect would still work, but it commits one extra render with the STALE
 * key visible before the reset lands; computing it inline means the new key
 * is already in place on the very render that shows the reopened drawer.
 */
export function useIdempotencyKey(open: boolean): [string, () => void] {
  const [key, setKey] = useState<string>(() => crypto.randomUUID());
  const [wasOpen, setWasOpen] = useState(open);

  if (open !== wasOpen) {
    setWasOpen(open);
    if (open) {
      setKey(crypto.randomUUID());
    }
  }

  const regenerate = useCallback(() => {
    setKey(crypto.randomUUID());
  }, []);

  return [key, regenerate];
}
