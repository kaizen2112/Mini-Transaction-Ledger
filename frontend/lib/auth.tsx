'use client';

import { createContext, useCallback, useContext, useMemo, useSyncExternalStore } from 'react';
import { useRouter } from 'next/navigation';
import { api } from '@/lib/api';
import {
  clearSession,
  getServerSnapshot,
  getSnapshot,
  setSession,
  subscribe,
  type AuthStatus,
} from '@/lib/session';
import type { UserSummary } from '@/types/api';

/**
 * Auth state for the whole app, per docs/12-frontend-plan.md §5.2.
 *
 * No useState and no useEffect: the session lives in localStorage, which is an
 * external store, so useSyncExternalStore reads it directly. Copying it into
 * component state inside an effect would render twice on every load and leave a
 * window where React's copy disagreed with the real value.
 *
 * `status` is three-valued on purpose. Without a distinct 'loading', the render
 * before storage has been read is indistinguishable from signed out, and the
 * route guard bounces a signed-in user to /login on every refresh.
 */

interface AuthContextValue {
  status: AuthStatus;
  user: UserSummary | null;
  signIn: (email: string, password: string) => Promise<void>;
  signOut: () => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const router = useRouter();
  const snapshot = useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot);

  const signIn = useCallback(
    async (email: string, password: string) => {
      // Errors propagate: the login form renders them next to the fields.
      const login = await api.auth.login({ email, password });
      setSession(login);
      router.replace('/');
    },
    [router],
  );

  const signOut = useCallback(() => {
    // Nothing to call server-side: the token is stateless, with no refresh
    // token and no server session to invalidate (contract §3). Signing out is
    // discarding it, and it expires within the hour regardless.
    clearSession();
    router.replace('/login');
  }, [router]);

  const value = useMemo<AuthContextValue>(
    () => ({
      status: snapshot.status,
      user: snapshot.session?.user ?? null,
      signIn,
      signOut,
    }),
    [snapshot, signIn, signOut],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used inside <AuthProvider>.');
  }
  return context;
}
