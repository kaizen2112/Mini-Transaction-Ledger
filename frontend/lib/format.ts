/**
 * Display formatting, per docs/12-frontend-plan.md §5.5.
 *
 * These functions format. They never compute. Every total the UI shows is a
 * total the API computed in SQL (BR-40) — deriving one here would eventually
 * disagree with the server, and the server would be right.
 */

const CURRENCY = 'BDT';

const money = new Intl.NumberFormat('en-US', {
  style: 'currency',
  currency: CURRENCY,
  currencyDisplay: 'code',
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

/** e.g. `BDT 1,250.00`. */
export function formatMoney(amount: number): string {
  return money.format(amount);
}

/**
 * Money with an explicit direction, e.g. `+BDT 1,000.00` / `−BDT 2,500.00`.
 *
 * Amounts are always positive in this system — direction lives in the type
 * (BR-03) — so the sign is presentation, applied from `type`, never read from
 * the number.
 */
export function formatSignedMoney(amount: number, type: 'Credit' | 'Debit'): string {
  return `${type === 'Credit' ? '+' : '−'}${money.format(amount)}`;
}

const dateOnly = new Intl.DateTimeFormat(undefined, {
  year: 'numeric',
  month: 'short',
  day: 'numeric',
});

const dateAndTime = new Intl.DateTimeFormat(undefined, {
  year: 'numeric',
  month: 'short',
  day: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
});

/**
 * The API sends ISO 8601 UTC with a `Z`. These render in the viewer's local
 * zone, which is what a person means by "when did this happen".
 */
export function formatDate(iso: string): string {
  return dateOnly.format(new Date(iso));
}

export function formatDateTime(iso: string): string {
  return dateAndTime.format(new Date(iso));
}

const monthLabel = new Intl.DateTimeFormat(undefined, { month: 'short', year: 'numeric' });
const monthShort = new Intl.DateTimeFormat(undefined, { month: 'short' });

/**
 * `{ year: 2026, month: 9 }` from the monthly report rendered as "Sep 2026".
 *
 * Built with Date.UTC and read back in UTC: the report groups by UTC calendar
 * month (contract §7), so constructing a local-time date here would show the
 * wrong month to anyone west of Greenwich on the 1st.
 */
export function formatMonth(year: number, month: number): string {
  return monthLabel.format(new Date(Date.UTC(year, month - 1, 1)));
}

/** Just "Sep", for axis labels where the year is already established. */
export function formatMonthShort(year: number, month: number): string {
  return monthShort.format(new Date(Date.UTC(year, month - 1, 1)));
}

/** e.g. `+18%` / `-4%`. Sign is always explicit so a drop reads as a drop. */
export function formatPercent(fraction: number): string {
  const percent = Math.round(fraction * 100);
  return `${percent > 0 ? '+' : ''}${percent}%`;
}

/** A plain `YYYY-MM-DD` for `<input type="date">`, in UTC. */
export function toDateInputValue(date: Date): string {
  return date.toISOString().slice(0, 10);
}
