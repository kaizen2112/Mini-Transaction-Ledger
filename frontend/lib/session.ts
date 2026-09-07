import type { LoginResponse, UserSummary } from '@/types/api';

/**
 * Token storage, per docs/12-frontend-plan.md §5.2.
 *
 * localStorage, exposed as a React external store so components can read it
 * with useSyncExternalStore rather than copying it into state inside an effect.
 * That is not a style preference: mirroring an external value into useState
 * means a second render pass on every load and a window where React's copy and
 * the real value disagree.
 *
 * The honest tradeoff of storing a token at all: it is readable by any XSS on
 * this origin. The mitigations are structural rather than cosmetic — the token
 * lives 60 minutes, there is NO refresh token (contract §3), and the API
 * re-derives the user from the `sub` claim on every request (BR-06). A stolen
 * token cannot be extended and cannot be used to act as anyone else.
 *
 * The rejected alternative was an httpOnly cookie set by a Next.js route
 * handler acting as a BFF proxy. Genuinely more secure, and it doubles the
 * moving parts: every call gets a second hop and the app stops being
 * explainable as "React calls the API".
 */

const STORAGE_KEY = 'ledger.session';

export interface Session {
  accessToken: string;
  /** ISO 8601 UTC. */
  expiresAt: string;
  user: UserSummary;
}

export type AuthStatus = 'loading' | 'authenticated' | 'anonymous';

export interface AuthSnapshot {
  status: AuthStatus;
  session: Session | null;
}

/**
 * Both snapshots are module constants so their identity is stable. A snapshot
 * getter that returns a fresh object on every call makes useSyncExternalStore
 * re-render forever.
 */
const LOADING: AuthSnapshot = { status: 'loading', session: null };
const ANONYMOUS: AuthSnapshot = { status: 'anonymous', session: null };

let snapshot: AuthSnapshot = LOADING;
let loaded = false;

const listeners = new Set<() => void>();

function isSession(value: unknown): value is Session {
  if (typeof value !== 'object' || value === null) return false;
  const candidate = value as Partial<Session>;
  return (
    typeof candidate.accessToken === 'string' &&
    typeof candidate.expiresAt === 'string' &&
    typeof candidate.user === 'object' &&
    candidate.user !== null
  );
}

/** True when `expiresAt` has passed. 60-minute token, no refresh path. */
export function isExpired(session: Session): boolean {
  const expiry = Date.parse(session.expiresAt);
  return Number.isNaN(expiry) || expiry <= Date.now();
}

function readStorage(): Session | null {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    const parsed: unknown = raw ? JSON.parse(raw) : null;
    return isSession(parsed) ? parsed : null;
  } catch {
    // Malformed JSON, or storage blocked entirely (private mode, a browser set
    // to refuse site data). Treat it as signed out rather than crashing the
    // whole app on boot.
    return null;
  }
}

function removeStorage(): void {
  try {
    window.localStorage.removeItem(STORAGE_KEY);
  } catch {
    // Nothing to do — the in-memory snapshot is authoritative from here.
  }
}

/**
 * Reads storage exactly once, then serves from the snapshot.
 *
 * Deliberately does NOT notify listeners: this runs during render, and calling
 * a subscriber from there is how you get an infinite loop.
 */
function ensureLoaded(): void {
  if (loaded) return;
  loaded = true;

  const stored = readStorage();

  if (!stored) {
    snapshot = ANONYMOUS;
    return;
  }

  if (isExpired(stored)) {
    // Expired before the app even started. Drop it here so no request is ever
    // made with a token the server would reject anyway.
    removeStorage();
    snapshot = ANONYMOUS;
    return;
  }

  snapshot = { status: 'authenticated', session: stored };
}

/** For useSyncExternalStore. Stable identity between actual changes. */
export function getSnapshot(): AuthSnapshot {
  if (typeof window === 'undefined') return LOADING;
  ensureLoaded();
  return snapshot;
}

/**
 * Server render and hydration both see 'loading'. That is what makes the route
 * guard hold still instead of bouncing a signed-in user to /login on every
 * refresh, before storage has been read.
 */
export function getServerSnapshot(): AuthSnapshot {
  return LOADING;
}

export function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

function publish(next: AuthSnapshot): void {
  snapshot = next;
  loaded = true;
  for (const listener of listeners) listener();
}

export function setSession(login: LoginResponse): void {
  const session: Session = {
    accessToken: login.accessToken,
    expiresAt: login.expiresAt,
    user: login.user,
  };

  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(session));
  } catch {
    // Storage unavailable. The in-memory snapshot still works for this tab, so
    // the user is signed in until they reload — better than refusing to sign in.
  }

  publish({ status: 'authenticated', session });
}

export function clearSession(): void {
  removeStorage();
  publish(ANONYMOUS);
}

/**
 * The token for the next request, or null. Imperative, for the API client —
 * components read the snapshot instead.
 */
export function getToken(): string | null {
  const current = getSnapshot().session;
  if (!current) return null;

  if (isExpired(current)) {
    clearSession();
    return null;
  }

  return current.accessToken;
}
