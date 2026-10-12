import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import HelpIcon from '@mui/icons-material/HelpOutlineOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Box, Card, CardContent, Typography } from '@mui/material';
import { alpha } from '@mui/material/styles';
import { useId } from 'react';
import type { BarsOverview } from '@/shared/api';
import { formatAgo, formatDate, formatIstDateTime, formatNumber } from '@/shared/lib/format';
import { numeric } from '@/theme';

interface Look {
  tone: 'success' | 'warning' | 'neutral';
  icon: typeof CheckCircleIcon;
  text: string;
}

function lookOf(bars: BarsOverview): Look {
  if (bars.lastBar === null) return { tone: 'neutral', icon: HelpIcon, text: 'No bars stored yet' };
  const session = formatDate(bars.expectedSession);
  if (bars.stale) {
    const days = bars.daysBehind;
    const by = days === null ? 'Behind' : `Behind by ${String(days)} trading ${days === 1 ? 'day' : 'days'}`;
    return { tone: 'warning', icon: WarningIcon, text: `${by} · expected the ${session} session` };
  }
  if (bars.instrumentsBehind > 0) {
    const count = formatNumber(bars.instrumentsBehind);
    const noun = bars.instrumentsBehind === 1 ? 'instrument' : 'instruments';
    return { tone: 'warning', icon: WarningIcon, text: `Newest bar is current — ${count} ${noun} behind` };
  }
  return { tone: 'success', icon: CheckCircleIcon, text: `Up to date · the ${session} session is in` };
}

/**
 * The one figure the page is about: when the newest stored 1-minute bar starts, in India time, with how long ago that
 * is. Behind or current is said in words and with an icon, and the card takes the warning or success colour too. "Up to date"
 * is only said when the newest bar is current and no instrument is behind.
 */
export function LastTick({ bars }: { bars: BarsOverview }) {
  const titleId = useId();
  const { tone, icon: Icon, text } = lookOf(bars);
  const colour = tone === 'neutral' ? 'text.secondary' : `${tone}.main`;
  return (
    <Card
      component="section"
      aria-labelledby={titleId}
      sx={(theme) => ({
        gridColumn: { sm: 'span 2', lg: 'span 4' },
        borderLeft: 4,
        borderLeftStyle: 'solid',
        borderLeftColor: tone === 'neutral' ? 'divider' : `${tone}.main`,
        bgcolor: tone === 'neutral' ? undefined : alpha(theme.palette[tone].main, 0.06),
      })}
    >
      <CardContent>
        <Typography id={titleId} variant="body2" color="text.secondary" sx={{ fontWeight: 600 }}>
          Last tick
        </Typography>
        {bars.lastBar && (
          <Typography component="div" sx={{ fontSize: '1.4rem', fontWeight: 700, mt: 0.5, ...numeric }}>
            <time dateTime={bars.lastBar}>{formatIstDateTime(bars.lastBar)}</time>
            <Typography component="span" color="text.secondary" sx={{ ml: 1, fontSize: '1rem', fontWeight: 500 }}>
              {formatAgo(bars.lastBar)}
            </Typography>
          </Typography>
        )}
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75, mt: 0.5, color: colour }}>
          <Icon fontSize="small" aria-hidden />
          <Typography variant="body2" component="span" sx={{ fontWeight: 600, color: 'inherit' }}>
            {text}
          </Typography>
        </Box>
      </CardContent>
    </Card>
  );
}
