import type { InputHTMLAttributes, SelectHTMLAttributes, ReactNode } from 'react';
import { useId } from 'react';

const CONTROL =
  'w-full rounded-lg border bg-surface px-3 py-2 text-sm text-ink placeholder:text-ink-faint ' +
  'focus:outline-2 focus:outline-offset-0 focus:outline-brand disabled:opacity-60';

function Shell({
  label,
  htmlFor,
  error,
  hint,
  children,
}: {
  label: string;
  htmlFor: string;
  error?: string;
  hint?: ReactNode;
  children: ReactNode;
}) {
  return (
    <div className="space-y-1.5">
      <label htmlFor={htmlFor} className="block text-sm font-medium text-ink">
        {label}
      </label>
      {children}
      {/*
        role="alert" so a screen reader announces a validation failure that
        arrived from the server after submit, not just on focus.
      */}
      {error ? (
        <p role="alert" className="text-xs text-debit">
          {error}
        </p>
      ) : hint ? (
        <p className="text-xs text-ink-faint">{hint}</p>
      ) : null}
    </div>
  );
}

interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  label: string;
  error?: string;
  hint?: ReactNode;
}

export function Input({ label, error, hint, className = '', ...rest }: InputProps) {
  const id = useId();
  return (
    <Shell label={label} htmlFor={id} error={error} hint={hint}>
      <input
        id={id}
        aria-invalid={error ? true : undefined}
        className={`${CONTROL} ${error ? 'border-debit' : 'border-line-strong'} ${className}`}
        {...rest}
      />
    </Shell>
  );
}

interface SelectProps extends SelectHTMLAttributes<HTMLSelectElement> {
  label: string;
  error?: string;
  hint?: ReactNode;
  children: ReactNode;
}

export function Select({ label, error, hint, children, className = '', ...rest }: SelectProps) {
  const id = useId();
  return (
    <Shell label={label} htmlFor={id} error={error} hint={hint}>
      <select
        id={id}
        aria-invalid={error ? true : undefined}
        className={`${CONTROL} ${error ? 'border-debit' : 'border-line-strong'} ${className}`}
        {...rest}
      >
        {children}
      </select>
    </Shell>
  );
}
