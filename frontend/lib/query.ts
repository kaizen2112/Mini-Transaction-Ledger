/**
 * Query-string helpers shared by every paginated/filterable list, per
 * docs/12-frontend-plan.md §4.4 and §4.5.
 */

/** BR-39: pageSize is one of these three, never a free-typed number. */
export const PAGE_SIZE_OPTIONS = [20, 50, 100] as const;
export type PageSize = (typeof PAGE_SIZE_OPTIONS)[number];

export const DEFAULT_PAGE_SIZE: PageSize = 20;

/**
 * Serialises a flat params object to a query string, dropping undefined, null
 * and empty-string values so an unset filter never becomes `?category=`
 * (which would bind to an empty string, not "no filter").
 */
export function toQueryString(params: Record<string, string | number | undefined | null>): string {
  const search = new URLSearchParams();

  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === '') continue;
    search.set(key, String(value));
  }

  const query = search.toString();
  return query ? `?${query}` : '';
}

/**
 * A plain `<input type="date">` value ("2026-01-01") turned into the UTC
 * instant bounding that calendar day, per §5.5 ("send from/to filters as UTC
 * ISO strings"). `from` gets the day's start, `to` gets its end, so a range
 * picked as "Jan 1 to Jan 1" includes the whole day rather than zero seconds
 * of it.
 */
export function startOfDayUtc(dateOnly: string): string {
  return `${dateOnly}T00:00:00.000Z`;
}

export function endOfDayUtc(dateOnly: string): string {
  return `${dateOnly}T23:59:59.999Z`;
}
