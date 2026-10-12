import ScheduleIcon from '@mui/icons-material/DateRangeOutlined';
import ShowChartIcon from '@mui/icons-material/ShowChartOutlined';
import StackedBarIcon from '@mui/icons-material/BarChartOutlined';
import { Box } from '@mui/material';
import type { DataOverview } from '@/shared/api';
import { formatCompact, formatIstDate, formatNumber } from '@/shared/lib/format';
import { StatTile } from '@/shared/ui';
import { AccountTile } from './AccountTile';
import { LastTick } from './LastTick';

const dash = '—';

function holes({ instrumentsBehind, instrumentsEmpty }: DataOverview['bars']): string {
  const parts = [
    instrumentsBehind > 0 ? `${formatNumber(instrumentsBehind)} behind` : null,
    instrumentsEmpty > 0 ? `${formatNumber(instrumentsEmpty)} without bars` : null,
  ].filter((part): part is string => part !== null);
  return parts.length > 0 ? parts.join(' · ') : 'All up to date';
}

/** How fresh the data is: the last tick first, then how much there is and the state of the Data account. */
export function FreshnessStrip({ overview }: { overview: DataOverview }) {
  const { bars, dhan } = overview;
  const covered =
    bars.firstBar && bars.lastBar ? `${formatIstDate(bars.firstBar)} – ${formatIstDate(bars.lastBar)}` : dash;
  return (
    <Box
      component="section"
      aria-label="Data freshness"
      sx={{
        display: 'grid',
        gap: 2,
        mb: 2,
        gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, 1fr)', lg: 'repeat(4, 1fr)' },
      }}
    >
      <LastTick bars={bars} />
      <StatTile
        label="Instruments with data"
        value={formatNumber(bars.instruments)}
        hint={holes(bars)}
        icon={ShowChartIcon}
        hue="instruments"
      />
      <StatTile
        label="1-minute bars"
        value={bars.totalBars > 0 ? formatCompact(bars.totalBars) : dash}
        hint={bars.totalBars > 0 ? `${formatNumber(bars.totalBars)} in total` : 'None stored yet'}
        icon={StackedBarIcon}
        hue="data"
      />
      <StatTile
        label="Covered"
        value={
          <Box component="span" sx={{ fontSize: '1rem' }}>
            {covered}
          </Box>
        }
        hint="First to last bar, India time"
        icon={ScheduleIcon}
        hue="sky"
      />
      <AccountTile dhan={dhan} />
    </Box>
  );
}
