import { Box, Chip, Stack, Typography } from '@mui/material';
import { MetricValue } from '@/shared/analytics';
import type { Schemas } from '@/shared/api';
import { formatNumber, formatPercent, formatPrice } from '@/shared/lib/format';
import { execMetric } from '../lib/metrics';
import { stateLook } from '../lib/stateWords';

type Row = Schemas['BlotterRow'];

/** The state as an icon and a word; a word outside the vocabulary is shown as it came, in the error style. */
export function StateCell({ row }: { row: Row }) {
  const look = row.stateKnown ? stateLook(row.state) : null;
  if (!look) {
    return (
      <Typography component="span" variant="body2" sx={{ color: 'error.main', fontWeight: 600 }}>
        Unknown state: {row.state}
      </Typography>
    );
  }
  const Icon = look.icon;
  return (
    <Stack component="span" direction="row" sx={{ gap: 0.5, alignItems: 'center' }}>
      <Icon aria-hidden fontSize="small" />
      {look.word}
    </Stack>
  );
}

export function SymbolCell({ row }: { row: Row }) {
  return (
    <>
      {row.symbol}
      {row.canary && <Chip size="small" label="canary" sx={{ ml: 1 }} />}
    </>
  );
}

export function FillCell({ row }: { row: Row }) {
  if (row.fillPrice === null) return <Box component="span">No fill</Box>;
  return (
    <Box component="span">
      {row.filledQuantity} at {formatPrice(row.fillPrice)}
      <br />
      Slippage <MetricValue state={execMetric(row.slippageTicks)} format={(v) => `${formatNumber(v)} ticks`} /> /{' '}
      <MetricValue state={execMetric(row.slippagePercent)} format={(v) => `${formatPercent(v)} %`} />
    </Box>
  );
}

export function CodesCell({ row }: { row: Row }) {
  const codes = [...row.rejectionCodes, ...(row.brokerCode ? [row.brokerCode] : [])];
  return (
    <Box component="span">
      {codes.length > 0 ? codes.join(', ') : 'None'}
      {row.algoId && (
        <>
          <br />
          algoId {row.algoId}
        </>
      )}
    </Box>
  );
}
