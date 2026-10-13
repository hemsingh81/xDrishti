import HistoryIcon from '@mui/icons-material/HistoryOutlined';
import ScheduleIcon from '@mui/icons-material/AccessTimeOutlined';
import SyncDisabledIcon from '@mui/icons-material/SyncDisabledOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Link, Stack, Typography } from '@mui/material';
import type { ReactElement } from 'react';
import { Link as RouterLink } from 'react-router';
import type { HoldingsSummary, ServiceView } from '@/shared/api';
import { numeric } from '@/theme';
import { describePrices, type PricesKind } from '../lib/prices';

const looks: Record<PricesKind, { icon: ReactElement; color: string }> = {
  none: { icon: <HistoryIcon fontSize="small" />, color: 'text.secondary' },
  fresh: { icon: <ScheduleIcon fontSize="small" />, color: 'success.main' },
  delayed: { icon: <WarningIcon fontSize="small" />, color: 'warning.main' },
  asOf: { icon: <HistoryIcon fontSize="small" />, color: 'text.secondary' },
  off: { icon: <SyncDisabledIcon fontSize="small" />, color: 'warning.main' },
};

interface PricesLineProps {
  summary: HoldingsSummary;
  /** The India wall clock (ms, to the minute) and whether the market is open. */
  now: number;
  marketOpen: boolean;
  /** The quote service; null while unknown. */
  quotes: ServiceView | null;
  /** The services list could not be read: the line says so rather than guessing that quotes are on. */
  quotesUnknown: boolean;
}

/**
 * Under the totals: how fresh the prices are and where they come from ("Prices as of 10:35 IST · quotes every 5 min").
 * It follows the minute clock, so it changes at most once a minute and is not a live region.
 */
export function PricesLine({ summary, now, marketOpen, quotes, quotesUnknown }: PricesLineProps) {
  if (summary.openCount === 0) return null;
  const { kind, text, asOf } = describePrices({ newest: summary.pricesAsOf.newest, now, marketOpen, quotes });
  const look = looks[kind];
  return (
    <Stack
      direction="row"
      sx={{ mb: 1, alignItems: 'center', flexWrap: 'wrap', columnGap: 1, rowGap: 0.25, flexShrink: 0 }}
    >
      <Stack direction="row" sx={{ gap: 0.75, alignItems: 'flex-start', minWidth: 0 }}>
        <Stack component="span" sx={{ color: look.color, flexShrink: 0 }} aria-hidden>
          {look.icon}
        </Stack>
        <Typography variant="body2" sx={{ ...numeric, pt: 0.25 }}>
          {asOf ? `${text} · ${asOf}` : text}
        </Typography>
      </Stack>
      {kind === 'off' && (
        <Link component={RouterLink} to="/services" variant="body2">
          Open Services
        </Link>
      )}
      {quotesUnknown && (
        <Typography variant="caption" color="text.secondary">
          The quote service status could not be read.
        </Typography>
      )}
    </Stack>
  );
}
