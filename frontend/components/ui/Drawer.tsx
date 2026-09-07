'use client';

import { useEffect, useRef, type ReactNode } from 'react';

/**
 * The right-hand slide-over used for every create form, from the reference
 * design.
 *
 * Deliberately not <dialog>: a native modal dialog traps focus well but its
 * backdrop and animation are awkward to style consistently, and this needs to
 * look like the reference. Focus handling is done explicitly below instead.
 */
export function Drawer({
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

  // Every keystroke inside the form re-renders whichever drawer is open, and
  // that drawer passes a freshly-created `close` function as `onClose` on
  // every render (it is not wrapped in useCallback — it does not need to be,
  // for anything except this effect). If `onClose` sat in the dependency
  // array below, that would re-run this effect on every keystroke, and the
  // effect's own querySelector(...).focus() would steal focus onto the
  // panel's first focusable element — the header's Close button, since it
  // comes before the form in DOM order — away from whatever field the user
  // was typing in. A ref sidesteps that: the effect only re-runs when `open`
  // itself changes, while the keydown handler still calls the latest onClose.
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
    // Move focus into the panel so the first Tab lands on the form, not on
    // whatever was behind the overlay.
    panel.current?.querySelector<HTMLElement>('input, select, button')?.focus();

    const { overflow } = document.body.style;
    document.body.style.overflow = 'hidden';

    return () => {
      document.removeEventListener('keydown', onKeyDown);
      document.body.style.overflow = overflow;
    };
  }, [open]);

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-50 flex justify-end">
      <div
        className="absolute inset-0 bg-ink/25"
        onClick={onClose}
        aria-hidden="true"
      />
      <div
        ref={panel}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className="relative flex h-full w-full max-w-md flex-col bg-surface shadow-xl"
      >
        <div className="flex items-center justify-between border-b border-line px-5 py-4">
          <h2 className="text-lg font-semibold text-ink">{title}</h2>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="rounded-md p-1 text-ink-faint hover:bg-canvas hover:text-ink"
          >
            <svg viewBox="0 0 20 20" className="size-5" fill="none" stroke="currentColor" strokeWidth="1.6">
              <path d="M5 5l10 10M15 5L5 15" strokeLinecap="round" />
            </svg>
          </button>
        </div>
        <div className="flex-1 overflow-y-auto px-5 py-5">{children}</div>
      </div>
    </div>
  );
}
