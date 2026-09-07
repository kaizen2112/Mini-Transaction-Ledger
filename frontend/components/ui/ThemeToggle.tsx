'use client';

import { useSyncExternalStore } from 'react';
import { getServerSnapshot, getSnapshot, setTheme, subscribe } from '@/lib/theme';

/**
 * A single light/dark switch, not a three-way light/dark/system picker —
 * simpler to explain, and "system" is already what a first-time visitor gets
 * for free from globals.css's prefers-color-scheme block. Picking here only
 * ever means overriding that default.
 */
export function ThemeToggle() {
  const theme = useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot);

  if (theme === null) {
    return <span className="block h-8 w-8" aria-hidden="true" />;
  }

  const next = theme === 'dark' ? 'light' : 'dark';

  return (
    <button
      type="button"
      onClick={() => setTheme(next)}
      aria-label={`Switch to ${next} mode`}
      title={`Switch to ${next} mode`}
      className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg text-ink-soft transition-colors hover:bg-canvas hover:text-ink"
    >
      {theme === 'dark' ? (
        <svg aria-hidden="true" viewBox="0 0 20 20" fill="currentColor" className="h-4.5 w-4.5">
          <path d="M10 3a1 1 0 0 1 1 1v1a1 1 0 1 1-2 0V4a1 1 0 0 1 1-1Zm0 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8Zm7-4a1 1 0 0 1-1 1h-1a1 1 0 1 1 0-2h1a1 1 0 0 1 1 1ZM5 10a1 1 0 0 1-1 1H3a1 1 0 1 1 0-2h1a1 1 0 0 1 1 1Zm10.66-5.66a1 1 0 0 1 0 1.42l-.7.7a1 1 0 1 1-1.42-1.42l.7-.7a1 1 0 0 1 1.42 0ZM6.46 14.24a1 1 0 0 1 0 1.42l-.7.7a1 1 0 1 1-1.42-1.42l.7-.7a1 1 0 0 1 1.42 0Zm9.2 2.12a1 1 0 0 1-1.42 0l-.7-.7a1 1 0 1 1 1.42-1.42l.7.7a1 1 0 0 1 0 1.42ZM5.76 5.76a1 1 0 0 1-1.42 0l-.7-.7a1 1 0 0 1 1.42-1.42l.7.7a1 1 0 0 1 0 1.42ZM10 16a1 1 0 0 1 1 1v1a1 1 0 1 1-2 0v-1a1 1 0 0 1 1-1Z" />
        </svg>
      ) : (
        <svg aria-hidden="true" viewBox="0 0 20 20" fill="currentColor" className="h-4.5 w-4.5">
          <path d="M17.293 13.293a8 8 0 0 1-10.586-10.586 8.001 8.001 0 1 0 10.586 10.586Z" />
        </svg>
      )}
    </button>
  );
}
