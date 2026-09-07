import { PAGE_SIZE_OPTIONS, type PageSize } from '@/lib/query';

/**
 * Shared by /transactions and /transfers (contract §5/§6 both paginate the
 * same way). pageSize is a fixed choice of three, never free-typed: BR-39
 * rejects an out-of-range value rather than clamping it, so offering only
 * values the API accepts means the control can never produce that error.
 */
export function Pagination({
  page,
  pageSize,
  totalItems,
  totalPages,
  onPageChange,
  onPageSizeChange,
}: {
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  onPageChange: (page: number) => void;
  onPageSizeChange: (pageSize: PageSize) => void;
}) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-3 border-t border-line px-4 py-3 text-sm">
      <p className="text-ink-faint">
        {totalItems === 0
          ? 'No results'
          : `Showing ${(page - 1) * pageSize + 1}-${Math.min(page * pageSize, totalItems)} of ${totalItems}`}
      </p>

      <div className="flex items-center gap-3">
        <label className="flex items-center gap-2 text-ink-soft">
          Rows per page
          <select
            value={pageSize}
            onChange={(event) => onPageSizeChange(Number(event.target.value) as PageSize)}
            className="rounded-lg border border-line-strong bg-surface px-2 py-1 text-sm focus:outline-2 focus:outline-brand"
          >
            {PAGE_SIZE_OPTIONS.map((size) => (
              <option key={size} value={size}>
                {size}
              </option>
            ))}
          </select>
        </label>

        <div className="flex items-center gap-1">
          <button
            type="button"
            onClick={() => onPageChange(page - 1)}
            disabled={page <= 1}
            className="rounded-lg border border-line-strong px-2.5 py-1 text-ink-soft hover:bg-canvas disabled:cursor-not-allowed disabled:opacity-50"
          >
            Previous
          </button>
          <span className="px-2 text-ink-faint">
            Page {page} of {Math.max(totalPages, 1)}
          </span>
          <button
            type="button"
            onClick={() => onPageChange(page + 1)}
            disabled={page >= totalPages}
            className="rounded-lg border border-line-strong px-2.5 py-1 text-ink-soft hover:bg-canvas disabled:cursor-not-allowed disabled:opacity-50"
          >
            Next
          </button>
        </div>
      </div>
    </div>
  );
}
