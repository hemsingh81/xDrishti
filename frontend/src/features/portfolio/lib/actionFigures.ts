import type { ActionItem } from '@/shared/api';
import { formatDate, formatInr, formatNumber, formatShare } from '@/shared/lib/format';
import { signedInr, signedPercent } from './figures';

export interface ActionFigure {
  label: string;
  value: string;
}

type Maybe = number | null | undefined;

const known = (value: Maybe): value is number => value !== null && value !== undefined;

const money = (value: Maybe): string | null => (known(value) ? formatInr(value) : null);

const share = (value: Maybe): string | null => (known(value) ? `${formatShare(value)}%` : null);

const count = (value: Maybe): string | null => (known(value) ? formatNumber(value) : null);

/** Drops the figures the item has no number for. */
const present = (figures: readonly (ActionFigure | null)[]): ActionFigure[] =>
  figures.filter((figure): figure is ActionFigure => figure !== null);

const fact = (label: string, value: string | null): ActionFigure | null => (value === null ? null : { label, value });

/** A gain or loss in rupees and percent: "−₹4,200 (−23.40%)"; whichever is known. */
function pnlText(pnl: Maybe, pct: Maybe): string | null {
  const amount = known(pnl) ? signedInr(pnl) : null;
  const percent = known(pct) ? signedPercent(pct) : null;
  if (amount && percent) return `${amount} (${percent})`;
  return amount ?? percent;
}

/** The pp gap with a real minus sign: "−12.7 pp". */
const points = (value: number): string => `${value < 0 ? '−' : '+'}${formatShare(Math.abs(value))} pp`;

/**
 * The figures that made an item fire, as short label/value pairs for the row (the sentences are the API's `title` and
 * `detail`). Which numbers exist depends on the rule; the ones the API left out are not shown.
 */
export function figuresOf(item: ActionItem): ActionFigure[] {
  const n = item.numbers;
  switch (item.rule) {
    case 'below_cost':
    case 'large_gain':
      return present([
        fact('Profit / loss', pnlText(n.pnl, n.pnlPct)),
        fact('Invested', money(n.invested)),
        fact('Value', money(n.value)),
      ]);
    case 'overweight':
      return present([
        fact('Weight', share(n.weightPct)),
        fact('Limit', share(n.thresholdPct)),
        fact('Positions in the account', count(n.positions)),
        fact('Value', money(n.value)),
      ]);
    case 'sector_concentration':
      return present([
        fact('Sector', n.sector ?? null),
        fact('Share of the account', share(n.sectorSharePct)),
        fact('Limit', share(n.thresholdPct)),
        fact('Sector value', money(n.sectorValue)),
        fact('Holdings in the sector', count(n.positions)),
        fact('Largest value', money(n.largestValue)),
      ]);
    case 'long_term_soon':
      return (n.lots ?? []).map((lot) => ({
        label: `${formatNumber(lot.qty)} ${lot.qty === 1 ? 'share' : 'shares'} bought ${formatDate(lot.acquiredOn)}`,
        value: `12 months on ${formatDate(lot.longTermOn)} (${
          lot.daysLeft === 1 ? 'in 1 day' : lot.daysLeft <= 0 ? 'today' : `in ${String(lot.daysLeft)} days`
        }), ${known(lot.gainPct) ? signedPercent(lot.gainPct) : 'free shares (no cost)'}`,
      }));
    case 'lagging_nifty':
      return present([
        fact('XIRR', known(n.xirrPct) ? signedPercent(n.xirrPct) : null),
        fact('Nifty 50 XIRR', known(n.benchmarkXirrPct) ? signedPercent(n.benchmarkXirrPct) : null),
        fact('Gap', known(n.gapPp) ? points(n.gapPp) : null),
        fact('Held', known(n.heldDays) ? `${formatNumber(n.heldDays)} days` : null),
      ]);
    case 'not_in_basket':
      return present([fact('Invested', money(n.invested)), fact('Value', money(n.value))]);
    default:
      return [];
  }
}
