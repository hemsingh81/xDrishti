import HistoryIcon from '@mui/icons-material/HistoryOutlined';
import ScheduleIcon from '@mui/icons-material/AccessTimeOutlined';
import SyncDisabledIcon from '@mui/icons-material/SyncDisabledOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Link, Stack, Typography } from '@mui/material';
import { useMemo, type ReactElement } from 'react';
import { Link as RouterLink } from 'react-router';
import type { HoldingsSummary, ServiceView } from '@/shared/api';
import { useWallClock } from '@/shared/lib/useWallClock';
import type { LiveFeedStatus } from '@/shared/realtime';
import { numeric } from '@/theme';
import { describeChip } from '../lib/liveChip';
import type { MarketPhase } from '../lib/market';
import { describePrices, type PricesKind } from '../lib/prices';
import { LiveChip } from './LiveChip';

const looks: Record<PricesKind, { icon: ReactElement; color: string }> = {
  none: { icon: <HistoryIcon fontSize="small" />, color: 'text.secondary' },
  fresh: { icon: <ScheduleIcon fontSize="small" />, color: 'success.main' },
  delayed: { icon: <WarningIcon fontSize="small" />, color: 'warning.main' },
  asOf: { icon: <HistoryIcon fontSize="small" />, color: 'text.secondary' },
  off: { icon: <SyncDisabledIcon fontSize="small" />, color: 'warning.main' },
};

/** What the chip is made from besides the prices: the market's phase, the hub, the feed and the ticks of the shown holdings. */
export interface LiveLineInput {
  phase: MarketPhase;
  /** The hub follows the scope. */
  following: boolean;
  feed: LiveFeedStatus | null;
  /** Since when it follows (ms). */
  followingSince: number | null;
  /** Server time minus browser time (ms). */
  skewMs: number;
  /** The price time (ms) of the newest live tick for the shown holdings. */
  newestTickAt: number | null;
}

interface PricesLineProps {
  summary: HoldingsSummary;
  /** The India wall clock (ms, to the minute) and whether the market is open. */
  now: number;
  marketOpen: boolean;
  /** The quote service; null while unknown. */
  quotes: ServiceView | null;
  /** The services list could not be read: the line says so rather than guessing that quotes are on. */
  quotesUnknown: boolean;
  live: LiveLineInput;
}

/**
 * Under the totals: the state of the prices (Live, Pre-open, Delayed, Quotes, Closed) and how fresh they are and where they
 * come from ("Prices as of 10:35 IST · quotes every 5 min"). It follows the clock to the second, so the text changes at most
 * once a second (a new tick changes it at most as often as the hub pushes: once a second), and it is not a live region.
 */
export function PricesLine({ summary, now, marketOpen, quotes, quotesUnknown, live }: PricesLineProps) {
  const second = useWallClock(1000);
  const { phase, following, feed, followingSince, skewMs, newestTickAt } = live;
  const chip = useMemo(
    () => describeChip({ phase, following, feed, now: second + skewMs, newestTickAt, followingSince }),
    [phase, following, feed, second, skewMs, newestTickAt, followingSince],
  );
  if (summary.openCount === 0) return null;
  const { kind, text, asOf } = describePrices({ newest: summary.pricesAsOf.newest, now, marketOpen, quotes });
  const look = looks[kind];
  const prices = asOf ? `${text} · ${asOf}` : text;
  const line =
    chip.detail ?? (chip.kind === 'preopen' ? `${prices} · pre-open prices are indicative until the open` : prices);
  const replaced = chip.detail !== null;
  return (
    <Stack
      direction="row"
      sx={{ mb: 1, alignItems: 'center', flexWrap: 'wrap', columnGap: 1, rowGap: 0.25, flexShrink: 0 }}
    >
      <LiveChip chip={chip} />
      <Stack direction="row" sx={{ gap: 0.75, alignItems: 'flex-start', minWidth: 0 }}>
        {!replaced && (
          <Stack component="span" sx={{ color: look.color, flexShrink: 0 }} aria-hidden>
            {look.icon}
          </Stack>
        )}
        <Typography variant="body2" sx={{ ...numeric, pt: 0.25 }}>
          {line}
        </Typography>
      </Stack>
      {kind === 'off' && (
        <Link component={RouterLink} to="/services" variant="body2">
          Open Services
        </Link>
      )}
      {replaced && kind === 'off' && (
        <Typography variant="caption" color="warning.main">
          {text}
        </Typography>
      )}
      {chip.fallback && (
        <Typography variant="caption" color="text.secondary">
          Live updates are not available: reading every minute.
        </Typography>
      )}
      {quotesUnknown && (
        <Typography variant="caption" color="text.secondary">
          The quote service status could not be read.
        </Typography>
      )}
    </Stack>
  );
}
