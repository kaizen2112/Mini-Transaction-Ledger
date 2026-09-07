'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { useAuth } from '@/lib/auth';
import { ThemeToggle } from '@/components/ui/ThemeToggle';

/**
 * The sidebar shell from docs/12-frontend-plan.md §3.3 — flat, no
 * OVERVIEW/TRACK/PLAN grouping, because five items do not need sections.
 *
 * This array holds only routes that EXIST. A nav full of disabled links to
 * unbuilt pages is dead UI, and a nav full of live links to 404s is worse.
 */
const NAV = [
  { href: '/', label: 'Dashboard' },
  { href: '/accounts', label: 'Accounts' },
  { href: '/transactions', label: 'Transactions' },
  { href: '/transfers', label: 'Transfers' },
  { href: '/reports', label: 'Reports' },
];

export function AppShell({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const { user, signOut } = useAuth();

  return (
    // h-screen + overflow-hidden on the outer flex row is what stops the
    // WHOLE page (sidebar included) from scrolling together: with only
    // min-h-screen, the flex row grows past the viewport as content grows,
    // and the sidebar — just a flex sibling, not fixed — scrolls away with
    // it. Giving <main> its own overflow-y-auto makes it the only thing that
    // scrolls; the sidebar's height is pinned to the viewport instead.
    <div className="flex h-screen overflow-hidden">
      <aside className="flex h-screen w-60 shrink-0 flex-col overflow-y-auto border-r border-line bg-surface">
        <div className="flex items-center justify-between gap-2 px-5 py-5">
          <span className="text-lg font-semibold tracking-tight text-brand">Ledger</span>
          <ThemeToggle />
        </div>

        <nav className="flex-1 px-3">
          <ul className="space-y-1">
            {NAV.map((item) => {
              const active = pathname === item.href || pathname.startsWith(`${item.href}/`);
              return (
                <li key={item.href}>
                  <Link
                    href={item.href}
                    aria-current={active ? 'page' : undefined}
                    className={[
                      'block rounded-lg px-3 py-2 text-sm font-medium transition-colors',
                      active
                        ? 'bg-canvas text-ink'
                        : 'text-ink-soft hover:bg-canvas hover:text-ink',
                    ].join(' ')}
                  >
                    {item.label}
                  </Link>
                </li>
              );
            })}
          </ul>
        </nav>

        <div className="border-t border-line px-4 py-4">
          <p className="truncate text-sm font-medium text-ink">{user?.displayName}</p>
          <p className="truncate text-xs text-ink-faint">{user?.email}</p>
          <button
            type="button"
            onClick={signOut}
            className="mt-3 text-sm font-medium text-ink-soft underline underline-offset-2 hover:text-ink"
          >
            Sign out
          </button>
        </div>
      </aside>

      <main className="h-screen min-w-0 flex-1 overflow-y-auto">
        <div className="mx-auto max-w-6xl px-8 py-8">{children}</div>
      </main>
    </div>
  );
}

export function PageHeader({
  title,
  subtitle,
  action,
}: {
  title: string;
  subtitle?: string;
  action?: React.ReactNode;
}) {
  return (
    <div className="mb-6 flex items-start justify-between gap-4">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight text-ink">{title}</h1>
        {subtitle && <p className="mt-1 text-sm text-ink-soft">{subtitle}</p>}
      </div>
      {action}
    </div>
  );
}
