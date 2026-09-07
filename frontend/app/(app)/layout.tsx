'use client';

import { useEffect } from 'react';
import { usePathname, useRouter } from 'next/navigation';
import { AppShell } from '@/components/AppShell';
import { LoadingState } from '@/components/ui/Feedback';
import { useAuth } from '@/lib/auth';

/**
 * The route guard for every authenticated page, per
 * docs/12-frontend-plan.md §5.2.
 *
 * This is UX, not security. It stops a signed-out visitor seeing an empty
 * shell flash before the redirect; it protects nothing. The security is the
 * JWT check the API performs on every request (BR-06/BR-07), which is why this
 * guard is allowed to be wrong without anything leaking — a user who defeats it
 * reaches a page whose every fetch returns 401.
 */
export default function AuthenticatedLayout({ children }: { children: React.ReactNode }) {
  const { status } = useAuth();
  const router = useRouter();
  const pathname = usePathname();

  useEffect(() => {
    if (status === 'anonymous') {
      // `next` so a deep link survives the round trip through sign-in.
      router.replace(`/login?next=${encodeURIComponent(pathname)}`);
    }
  }, [status, router, pathname]);

  // 'loading' is the state before localStorage has been read. Rendering the
  // app here would flash content; redirecting here would bounce a signed-in
  // user out on every refresh.
  if (status !== 'authenticated') {
    return <LoadingState label="Checking your session…" />;
  }

  return <AppShell>{children}</AppShell>;
}
