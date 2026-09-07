import type { Metadata } from 'next';
import Script from 'next/script';
import { AuthProvider } from '@/lib/auth';
import './globals.css';

export const metadata: Metadata = {
  title: 'Ledger',
  description: 'Personal transaction ledger',
};

/*
 * Reads the saved theme BEFORE the page paints. Without this, the page always
 * paints light first (globals.css's default) and only flips to dark once
 * React hydrates and ThemeToggle's own effect runs — a visible flash on every
 * load for anyone who picked dark. `beforeInteractive` is Next's sanctioned
 * spot for exactly this: a tiny script that must run ahead of hydration.
 */
const THEME_INIT_SCRIPT = `
(function () {
  try {
    var theme = localStorage.getItem('ledger-theme');
    if (theme === 'light' || theme === 'dark') {
      document.documentElement.setAttribute('data-theme', theme);
    }
  } catch (e) {}
})();
`;

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <head>
        <Script id="theme-init" strategy="beforeInteractive">
          {THEME_INIT_SCRIPT}
        </Script>
      </head>
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
