import type { BarsOverview } from '@/shared/api';
import { formatIstDate } from '@/shared/lib/format';
import { RibbonStat } from './RibbonStat';

/** The period the stored bars cover, first to last bar in India time. */
export function CoveredPill({ bars }: { bars: BarsOverview }) {
  return (
    <RibbonStat label="Covered">
      <span title="First to last bar, India time">
        {bars.firstBar && bars.lastBar ? `${formatIstDate(bars.firstBar)} – ${formatIstDate(bars.lastBar)}` : '—'}
      </span>
    </RibbonStat>
  );
}
