import type { BarsOverview } from '@/shared/api';
import { formatDate, formatNumber } from '@/shared/lib/format';

export type TickTone = 'success' | 'warning' | 'neutral';

export interface TickLook {
  tone: TickTone;
  /** Which icon says it too: a check, a warning, or a question mark when nothing is stored. */
  kind: 'current' | 'behind' | 'none';
  text: string;
}

/**
 * How the newest stored 1-minute bar is described. "Up to date" is only said when the newest bar is current and no
 * instrument is behind; "behind" counts trading days (the calendar).
 */
export function tickLook(bars: BarsOverview): TickLook {
  if (bars.lastBar === null) return { tone: 'neutral', kind: 'none', text: 'No bars stored yet' };
  const session = formatDate(bars.expectedSession);
  if (bars.stale) {
    const days = bars.daysBehind;
    const by = days === null ? 'Behind' : `Behind by ${String(days)} trading ${days === 1 ? 'day' : 'days'}`;
    return { tone: 'warning', kind: 'behind', text: `${by} · expected the ${session} session` };
  }
  if (bars.instrumentsBehind > 0) {
    const count = formatNumber(bars.instrumentsBehind);
    const noun = bars.instrumentsBehind === 1 ? 'instrument' : 'instruments';
    return { tone: 'warning', kind: 'behind', text: `Newest bar is current — ${count} ${noun} behind` };
  }
  return { tone: 'success', kind: 'current', text: `Up to date · the ${session} session is in` };
}

/** "12 behind" and "3 without bars": what is missing among the instruments (null when nothing is). */
export function instrumentGaps({ instrumentsBehind, instrumentsEmpty }: BarsOverview) {
  return {
    behind: instrumentsBehind > 0 ? `${formatNumber(instrumentsBehind)} behind` : null,
    empty: instrumentsEmpty > 0 ? `${formatNumber(instrumentsEmpty)} without bars` : null,
  };
}
