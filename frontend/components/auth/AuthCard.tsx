import Link from 'next/link';
import type { ReactNode } from 'react';

/** Shared frame for /login and /register so the two pages cannot drift apart. */
export function AuthCard({
  title,
  subtitle,
  children,
  footer,
}: {
  title: string;
  subtitle: string;
  children: ReactNode;
  footer: { prompt: string; href: string; label: string };
}) {
  return (
    <main className="flex min-h-screen items-center justify-center px-4 py-10">
      <div className="w-full max-w-sm">
        <p className="mb-6 text-center text-lg font-semibold tracking-tight text-brand">Ledger</p>

        <div className="rounded-xl border border-line bg-surface px-6 py-6">
          <h1 className="text-xl font-semibold text-ink">{title}</h1>
          <p className="mt-1 text-sm text-ink-soft">{subtitle}</p>
          <div className="mt-5">{children}</div>
        </div>

        <p className="mt-5 text-center text-sm text-ink-soft">
          {footer.prompt}{' '}
          <Link href={footer.href} className="font-medium text-brand underline underline-offset-2">
            {footer.label}
          </Link>
        </p>
      </div>
    </main>
  );
}
