import type { ReactNode } from 'react';

type Tone = 'neutral' | 'credit' | 'debit' | 'warn' | 'brand';

const TONES: Record<Tone, string> = {
  neutral: 'bg-canvas text-ink-soft border-line-strong',
  credit: 'bg-credit-tint text-credit border-credit/20',
  debit: 'bg-debit-tint text-debit border-debit/20',
  warn: 'bg-warn-tint text-warn border-warn/20',
  brand: 'bg-brand-tint text-brand border-brand/20',
};

export function Badge({
  tone = 'neutral',
  children,
  title,
}: {
  tone?: Tone;
  children: ReactNode;
  title?: string;
}) {
  return (
    <span
      title={title}
      className={`inline-flex items-center rounded-full border px-2 py-0.5 text-xs font-medium ${TONES[tone]}`}
    >
      {children}
    </span>
  );
}
