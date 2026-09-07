import { Badge } from '@/components/ui/Badge';
import { formatPercent } from '@/lib/format';

/**
 * Month-over-month change, per docs/12-frontend-plan.md §3.5.
 *
 * The reference design showed "↗ 100%" on every card, which is simply what you
 * get when the previous period was zero — a meaningless number rendered
 * confidently. So the rule here is explicit: a percentage ONLY when the
 * previous month is non-zero. Otherwise an em-dash, with the reason in the
 * tooltip. There is no honest percentage change from nothing.
 *
 * `goodWhenUp` differs per metric: income rising is good, expenses rising is
 * not. Colour follows meaning, not sign.
 */
export function DeltaBadge({
  current,
  previous,
  goodWhenUp = true,
}: {
  current: number;
  previous: number;
  goodWhenUp?: boolean;
}) {
  if (previous === 0) {
    return (
      <Badge tone="neutral" title="No prior month to compare against">
        —
      </Badge>
    );
  }

  const change = (current - previous) / previous;

  // Rounds to the same integer the label shows, so a +0.4% change reads as
  // "0%" in NEUTRAL grey rather than being coloured green for a rise the
  // number does not visibly show.
  const rounded = Math.round(change * 100);
  const tone = rounded === 0 ? 'neutral' : (rounded > 0) === goodWhenUp ? 'credit' : 'debit';

  return (
    <Badge tone={tone} title="Compared with last month">
      {formatPercent(change)}
    </Badge>
  );
}
