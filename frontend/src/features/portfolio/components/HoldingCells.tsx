import { Box, Chip, Stack, Tooltip, Typography } from '@mui/material';
import type { HoldingRow } from '@/shared/api';
import { formatDate, formatDateTime, formatInrPrecise } from '@/shared/lib/format';
import { monoFontFamily } from '@/theme';

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
    <Tooltip describeChild title={`Price as of ${formatDateTime(holding.lastPriceAt)}`}>
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
