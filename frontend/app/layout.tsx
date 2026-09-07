import type { Metadata } from 'next';
import { AuthProvider } from '@/lib/auth';
import './globals.css';

export const metadata: Metadata = {
  title: 'Ledger',
  description: 'Personal transaction ledger',
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <body className="antialiased">
        {/*
          AuthProvider wraps everything, including /login, so the login page can
          call signIn() through the same context the rest of the app uses.
        */}
        <AuthProvider>{children}</AuthProvider>
      </body>
    </html>
  );
}
