/**
 * Light/dark preference, exposed as a React external store — the same
 * pattern lib/session.ts uses for the auth token, and for the same reason:
 * useSyncExternalStore reads this directly, so there is no useState +
 * useEffect pair mirroring it into React state (which would render twice on
 * every load and briefly disagree with the real value).
 *
 * The colours themselves never wait on this module: globals.css's
 * prefers-color-scheme block and the root layout's blocking init script
 * already paint the right theme before React even runs. This store exists
 * only so ThemeToggle knows which icon and label to show.
 */

export type Theme = 'light' | 'dark';

const STORAGE_KEY = 'ledger-theme';

function readStorage(): Theme | null {
  try {
    const value = window.localStorage.getItem(STORAGE_KEY);
    return value === 'light' || value === 'dark' ? value : null;
  } catch {
    return null;
  }
}

function systemPreference(): Theme {
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

let snapshot: Theme | null = null;
let loaded = false;

const listeners = new Set<() => void>();

function ensureLoaded(): void {
  if (loaded) return;
  loaded = true;
  snapshot = readStorage() ?? systemPreference();
}

/** For useSyncExternalStore. */
export function getSnapshot(): Theme | null {
  if (typeof window === 'undefined') return null;
  ensureLoaded();
  return snapshot;
}

/**
 * Server render and hydration both see null — ThemeToggle renders an empty
 * placeholder for that one frame rather than guessing an icon that might not
 * match the theme the blocking script already applied to the DOM.
 */
export function getServerSnapshot(): Theme | null {
  return null;
}

export function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

export function setTheme(next: Theme): void {
  document.documentElement.setAttribute('data-theme', next);

  try {
    window.localStorage.setItem(STORAGE_KEY, next);
  } catch {
    // Storage unavailable (private mode, disabled site data). The DOM
    // attribute above still applies the choice for the rest of this load.
  }

  snapshot = next;
  loaded = true;
  for (const listener of listeners) listener();
}
