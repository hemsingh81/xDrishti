import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import EventBusyIcon from '@mui/icons-material/EventBusyOutlined';
import HelpIcon from '@mui/icons-material/HelpOutlineOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Box, IconButton, Tooltip, Typography } from '@mui/material';
import type { BarsOverview, CalendarDay } from '@/shared/api';
import { formatAgo, formatIstDateTime } from '@/shared/lib/format';
import { holidayLine } from '../lib/calendar';
import { tickLook } from '../lib/freshness';
import { PillLabel } from './RibbonPill';
import { pillSx } from './ribbonStyle';

const icons = { current: CheckCircleIcon, behind: WarningIcon, none: HelpIcon } as const;

/**
 * The one item of the ribbon the page is about: when the newest stored 1-minute bar starts, in India time, with how long
 * ago that is. Current or behind is said in words and with an icon as well as the colour. The next holiday is a tooltip
 * on a small calendar button (focusable, so a keyboard user gets it too).
 */
export function LastTick({ bars, nextHoliday = null }: { bars: BarsOverview; nextHoliday?: CalendarDay | null }) {
  const { tone, kind, text } = tickLook(bars);
  const Icon = icons[kind];
  const colour = tone === 'neutral' ? 'text.secondary' : `${tone}.main`;
  const holiday = nextHoliday ? `Next holiday: ${holidayLine(nextHoliday)}` : null;
  return (
    <Box component="section" aria-label="Last tick" sx={[pillSx(tone), { py: 0.5 }]}>
      <PillLabel>Last tick</PillLabel>
      {bars.lastBar && (
        <>
          <Typography component="time" dateTime={bars.lastBar} sx={{ fontWeight: 700 }}>
            {formatIstDateTime(bars.lastBar)}
          </Typography>
          <Typography component="span" variant="caption" color="text.secondary">
            {formatAgo(bars.lastBar)}
          </Typography>
        </>
      )}
      <Box component="span" sx={{ display: 'inline-flex', alignItems: 'center', gap: 0.5, color: colour }}>
        <Icon fontSize="small" aria-hidden />
        <Typography component="span" variant="body2" sx={{ fontWeight: 600, color: 'inherit' }}>
          {text}
        </Typography>
      </Box>
      {holiday && (
        <Tooltip title={holiday}>
          <IconButton size="small" aria-label={holiday} sx={{ p: 0.25 }}>
            <EventBusyIcon fontSize="small" />
          </IconButton>
        </Tooltip>
      )}
    </Box>
  );
}
