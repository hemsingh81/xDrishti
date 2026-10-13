import { Box, Chip, Stack, Tooltip, Typography } from '@mui/material';
import type { HoldingRow } from '@/shared/api';
import { formatDate, formatInrPrecise, formatIstDateTime, formatIstDayMonth } from '@/shared/lib/format';
import { useWallClock } from '@/shared/lib/useWallClock';
import { monoFontFamily, numeric } from '@/theme';
import { noDayChangeReasonOf } from '../lib/figures';
import { isEarlierDay } from '../lib/prices';
import { DayChangeAmount, PnlPercent } from './Figures';

const dash = '—';

const lotsText = (count: number) => `${String(count)} ${count === 1 ? 'lot' : 'lots'}`;

export function Symbol({ holding }: { holding: HoldingRow }) {
  return (
    <Stack direction="row" spacing={0.75} sx={{ alignItems: 'center', flexWrap: 'wrap', rowGap: 0.25 }}>
      <Box component="span" sx={{ fontFamily: monoFontFamily, fontWeight: 600 }}>
        {holding.symbol}
      </Box>
      {!holding.mapped && <Chip size="small" variant="outlined" color="warning" label="Not in master list" />}
      {holding.status === 'open' && !holding.priced && (
        <Chip size="small" variant="outlined" color="warning" label="No price yet" />
      )}
      {holding.status === 'closed' && <Chip size="small" variant="outlined" label="Closed" />}
    </Stack>
  );
}

export function LastPrice({ holding }: { holding: HoldingRow }) {
  if (!holding.priced) return <>{dash}</>;
  const price = formatInrPrecise(holding.lastPrice);
  if (holding.lastPriceAt === null) return <>{price}</>;
  return (
    <Tooltip describeChild title={`Price as of ${formatIstDateTime(holding.lastPriceAt)}`}>
      <span>{price}</span>
    </Tooltip>
  );
}

export function Acquired({ holding }: { holding: HoldingRow }) {
  return (
    <Stack direction="row" spacing={0.75} sx={{ alignItems: 'center', flexWrap: 'wrap', rowGap: 0.25 }}>
      <span>{holding.earliestAcquiredOn ? formatDate(holding.earliestAcquiredOn) : dash}</span>
      {holding.usesDefaultDate && <Chip size="small" variant="outlined" color="warning" label="Default date" />}
      <Typography component="span" variant="caption" color="text.secondary">
        {lotsText(holding.lotCount)}
      </Typography>
    </Stack>
  );
}

/** Day change: rupees over percent; the date of the session under them when the price is from an earlier India day. */
export function DayChange({ holding }: { holding: HoldingRow }) {
  const now = useWallClock(60_000);
  const stale = holding.priceAt !== null && isEarlierDay(holding.priceAt, now);
  return (
    <Stack sx={{ alignItems: 'flex-end', lineHeight: 1.25 }}>
      <DayChangeAmount value={holding.dayChange} reason={noDayChangeReasonOf(holding)} />
      {holding.dayChange !== null && (
        <Typography component="span" variant="caption" sx={numeric}>
          <PnlPercent value={holding.dayChangePct} />
        </Typography>
      )}
      {holding.dayChange !== null && stale && holding.priceAt !== null && (
        <Typography component="span" variant="caption" color="text.secondary">
          {formatIstDayMonth(holding.priceAt)}
        </Typography>
      )}
    </Stack>
  );
}
