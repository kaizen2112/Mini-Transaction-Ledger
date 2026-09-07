import { TRANSACTION_CATEGORIES, TRANSACTION_TYPES, type TransactionCategory, type TransactionType } from '@/types/api';

/**
 * All string-valued controlled-input state: dates are plain
 * `<input type="date">` values, amounts are the raw text the user typed. The
 * page converts these to the typed TransactionQueryParams (UTC instants,
 * numbers) when it builds the actual request — this component only edits
 * text, per docs/12-frontend-plan.md §4.4.
 */
export interface TransactionFilterState {
  type: '' | TransactionType;
  category: '' | TransactionCategory;
  from: string;
  to: string;
  minAmount: string;
  maxAmount: string;
  search: string;
}

export const EMPTY_FILTERS: TransactionFilterState = {
  type: '',
  category: '',
  from: '',
  to: '',
  minAmount: '',
  maxAmount: '',
  search: '',
};

const CONTROL =
  'w-full rounded-lg border border-line-strong bg-surface px-3 py-1.5 text-sm text-ink ' +
  'placeholder:text-ink-faint focus:outline-2 focus:outline-brand';

export function TransactionFilters({
  value,
  onChange,
  rangeError,
}: {
  value: TransactionFilterState;
  onChange: (next: TransactionFilterState) => void;
  /** FILTER_RANGE_INVALID, rendered here rather than as a page-level banner
   * because the offending controls are the date/amount fields right below it
   * (§5.4: "inline on the offending control"). */
  rangeError?: string;
}) {
  function set<K extends keyof TransactionFilterState>(key: K, val: TransactionFilterState[K]) {
    onChange({ ...value, [key]: val });
  }

  return (
    <div className="space-y-3 border-b border-line px-4 py-3">
      <div className="flex flex-wrap gap-3">
        <input
          type="search"
          value={value.search}
          onChange={(e) => set('search', e.target.value)}
          placeholder="Search description"
          aria-label="Search description"
          maxLength={100}
          className={`${CONTROL} max-w-xs`}
        />

        <select
          value={value.type}
          onChange={(e) => set('type', e.target.value as TransactionFilterState['type'])}
          aria-label="Filter by type"
          className={`${CONTROL} w-auto`}
        >
          <option value="">All types</option>
          {TRANSACTION_TYPES.map((option) => (
            <option key={option} value={option}>
              {option}
            </option>
          ))}
        </select>

        <select
          value={value.category}
          onChange={(e) => set('category', e.target.value as TransactionFilterState['category'])}
          aria-label="Filter by category"
          className={`${CONTROL} w-auto`}
        >
          <option value="">All categories</option>
          {/*
            Full list, including Transfer and Reversal: those are rejected as
            CREATE input (BR-24) but are perfectly valid FILTER values — a user
            may want to see just their transfer legs.
          */}
          {TRANSACTION_CATEGORIES.map((option) => (
            <option key={option} value={option}>
              {option}
            </option>
          ))}
        </select>
      </div>

      <div className="flex flex-wrap items-end gap-3">
        <label className="text-xs text-ink-faint">
          From
          <input
            type="date"
            value={value.from}
            onChange={(e) => set('from', e.target.value)}
            className={`${CONTROL} mt-1 w-auto`}
          />
        </label>
        <label className="text-xs text-ink-faint">
          To
          <input
            type="date"
            value={value.to}
            onChange={(e) => set('to', e.target.value)}
            className={`${CONTROL} mt-1 w-auto`}
          />
        </label>
        <label className="text-xs text-ink-faint">
          Min amount
          <input
            type="number"
            inputMode="decimal"
            min="0"
            step="0.01"
            value={value.minAmount}
            onChange={(e) => set('minAmount', e.target.value)}
            placeholder="0.00"
            className={`${CONTROL} mt-1 w-28`}
          />
        </label>
        <label className="text-xs text-ink-faint">
          Max amount
          <input
            type="number"
            inputMode="decimal"
            min="0"
            step="0.01"
            value={value.maxAmount}
            onChange={(e) => set('maxAmount', e.target.value)}
            placeholder="0.00"
            className={`${CONTROL} mt-1 w-28`}
          />
        </label>
        {(value.from || value.to || value.minAmount || value.maxAmount || value.type || value.category || value.search) && (
          <button
            type="button"
            onClick={() => onChange(EMPTY_FILTERS)}
            className="text-sm font-medium text-ink-soft underline underline-offset-2 hover:text-ink"
          >
            Clear filters
          </button>
        )}
      </div>

      {rangeError && (
        <p role="alert" className="text-xs text-debit">
          {rangeError}
        </p>
      )}
    </div>
  );
}
