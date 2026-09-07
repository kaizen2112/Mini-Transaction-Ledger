'use client';

import { useEffect, useRef, type ReactNode } from 'react';

/**
 * A small centered confirm modal — distinct from Drawer, which is a full-height
 * slide-over for create forms. Reversal (§4.4) needs a short "are you sure",
 * not a form panel, so this is a second, smaller primitive rather than
 * stretching Drawer to cover both shapes.
 */
export function ConfirmDialog({
  open,
  title,
  onClose,
  children,
}: {
  open: boolean;
  title: string;
  onClose: () => void;
  children: ReactNode;
}) {
  const panel = useRef<HTMLDivElement>(null);

  // Same fix as Drawer.tsx: onClose is a new function on every parent
  // re-render, so it cannot sit in this effect's dependency array without
  // re-running (and re-stealing focus) on every keystroke in the optional
  // description field. See the comment there for the full explanation.
  const onCloseRef = useRef(onClose);
  useEffect(() => {
    onCloseRef.current = onClose;
  });

  useEffect(() => {
    if (!open) return;

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onCloseRef.current();
    };

    document.addEventListener('keydown', onKeyDown);
    panel.current?.querySelector<HTMLElement>('button, input, textarea')?.focus();

    const { overflow } = document.body.style;
    document.body.style.overflow = 'hidden';

    return () => {
      document.removeEventListener('keydown', onKeyDown);
      document.body.style.overflow = overflow;
    };
  }, [open]);

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center px-4">
      <div className="absolute inset-0 bg-ink/25" onClick={onClose} aria-hidden="true" />
      <div
        ref={panel}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className="relative w-full max-w-sm rounded-xl border border-line bg-surface p-5 shadow-xl"
      >
        <h2 className="text-base font-semibold text-ink">{title}</h2>
        <div className="mt-3">{children}</div>
      </div>
    </div>
  );
}
