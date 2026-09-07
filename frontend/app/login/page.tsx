'use client';

import { Suspense, useEffect, useState, type FormEvent } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { AuthCard } from '@/components/auth/AuthCard';
import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Field';
import { ErrorNotice, LoadingState } from '@/components/ui/Feedback';
import { useAuth } from '@/lib/auth';
import { ApiError, fieldErrors, messageFor } from '@/lib/errors';

function LoginForm() {
  const { status, signIn } = useAuth();
  const router = useRouter();
  const params = useSearchParams();

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [submitting, setSubmitting] = useState(false);

  const registered = params.get('registered') === '1';
  const expired = params.get('expired') === '1';
  const next = params.get('next');

  useEffect(() => {
    // Already signed in — nothing to do here.
    if (status === 'authenticated') {
      router.replace(next && next.startsWith('/') ? next : '/');
    }
  }, [status, router, next]);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    setSubmitting(true);
    setError(null);

    try {
      await signIn(email, password);
    } catch (caught) {
      setError(caught);
      setSubmitting(false);
    }
  }

  const fields = fieldErrors(error);

  return (
    <AuthCard
      title="Sign in"
      subtitle="Welcome back to your ledger."
      footer={{ prompt: 'No account yet?', href: '/register', label: 'Create one' }}
    >
      {registered && (
        <div className="mb-4 rounded-lg border border-credit/25 bg-credit-tint px-3 py-2 text-sm text-ink">
          Account created. Sign in to continue.
        </div>
      )}
      {expired && !error && (
        <div className="mb-4 rounded-lg border border-warn/25 bg-warn-tint px-3 py-2 text-sm text-ink">
          Your session expired. Please sign in again.
        </div>
      )}

      <form onSubmit={onSubmit} className="space-y-4" noValidate>
        <Input
          label="Email"
          type="email"
          autoComplete="email"
          required
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          error={fields.email}
        />
        <Input
          label="Password"
          type="password"
          autoComplete="current-password"
          required
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          error={fields.password}
        />

        {/*
          One message for both unknown email and wrong password. BR-12 makes the
          backend deliberately indistinguishable between the two — including
          running a dummy hash so the timing matches — and the UI must not undo
          that by being more helpful.
        */}
        {error != null && Object.keys(fields).length === 0 && (
          <ErrorNotice
            message={messageFor(error)}
            traceId={error instanceof ApiError ? error.traceId : undefined}
          />
        )}

        <Button type="submit" loading={submitting} fullWidth>
          Sign in
        </Button>
      </form>
    </AuthCard>
  );
}

export default function LoginPage() {
  // useSearchParams requires a Suspense boundary in the App Router.
  return (
    <Suspense fallback={<LoadingState />}>
      <LoginForm />
    </Suspense>
  );
}
