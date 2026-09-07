'use client';

import { useState, type FormEvent } from 'react';
import { useRouter } from 'next/navigation';
import { AuthCard } from '@/components/auth/AuthCard';
import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Field';
import { ErrorNotice } from '@/components/ui/Feedback';
import { api } from '@/lib/api';
import { ApiError, fieldErrors, messageFor } from '@/lib/errors';

export default function RegisterPage() {
  const router = useRouter();

  const [email, setEmail] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [submitting, setSubmitting] = useState(false);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    setSubmitting(true);
    setError(null);

    try {
      await api.auth.register({ email, password, displayName });
      // Registration never returns a token (contract §3): registration and
      // login are separate so the login path has exactly one implementation.
      // So this hands off to /login rather than signing in here.
      router.replace('/login?registered=1');
    } catch (caught) {
      setError(caught);
      setSubmitting(false);
    }
  }

  const fields = fieldErrors(error);

  return (
    <AuthCard
      title="Create your account"
      subtitle="One account, as many ledgers as you need."
      footer={{ prompt: 'Already registered?', href: '/login', label: 'Sign in' }}
    >
      <form onSubmit={onSubmit} className="space-y-4" noValidate>
        <Input
          label="Display name"
          autoComplete="name"
          required
          maxLength={100}
          value={displayName}
          onChange={(e) => setDisplayName(e.target.value)}
          error={fields.displayname}
        />
        <Input
          label="Email"
          type="email"
          autoComplete="email"
          required
          maxLength={256}
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          error={fields.email}
        />
        <Input
          label="Password"
          type="password"
          autoComplete="new-password"
          required
          minLength={8}
          maxLength={128}
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          hint="8 to 128 characters."
          error={fields.password}
        />

        {error != null && Object.keys(fields).length === 0 && (
          <ErrorNotice
            message={messageFor(error)}
            traceId={error instanceof ApiError ? error.traceId : undefined}
          />
        )}

        <Button type="submit" loading={submitting} fullWidth>
          Create account
        </Button>
      </form>
    </AuthCard>
  );
}
