import type { BarsOverview } from '@/shared/api';
import { formatCompact, formatNumber } from '@/shared/lib/format';
import { RibbonStat } from './RibbonStat';

/** The total of 1-minute bars in short form; the full number is the figure's title. */
export function RibbonBarsPill({ bars }: { bars: BarsOverview }) {
  return (
    <RibbonStat label="1-minute bars">
      <span title={bars.totalBars > 0 ? `${formatNumber(bars.totalBars)} in total` : 'None stored yet'}>
        {bars.totalBars > 0 ? formatCompact(bars.totalBars) : '—'}
      </span>
    </RibbonStat>
  );
}
