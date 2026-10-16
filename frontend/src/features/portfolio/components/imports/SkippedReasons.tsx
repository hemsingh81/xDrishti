import { Box, Chip, Stack, Typography } from '@mui/material';
import { useId } from 'react';
import type { TradeFileSummary } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { skipReasonLabel } from '../../lib/reportImport';

/** The rows left out, by reason (futures and options, commodity, BSE only, covered by Dhan ...): counted, never silently dropped. */
export function SkippedReasons({ skipped }: { skipped: TradeFileSummary['skipped'] }) {
  const id = useId();
  const parts = Object.entries(skipped.byReason)
    .filter(([, count]) => count > 0)
    .sort(([, a], [, b]) => b - a);
  if (skipped.total === 0) return null;
  return (
    <Box>
      <Typography variant="body2" id={id}>
        {`${formatNumber(skipped.total)} rows are left out, by reason:`}
      </Typography>
      <Stack
        component="ul"
        aria-labelledby={id}
        direction="row"
        sx={{ m: 0, mt: 0.5, p: 0, gap: 0.5, flexWrap: 'wrap', listStyle: 'none' }}
      >
        {parts.map(([reason, count]) => (
          <li key={reason}>
            <Chip size="small" variant="outlined" label={`${skipReasonLabel(reason)}: ${formatNumber(count)}`} />
          </li>
        ))}
      </Stack>
    </Box>
  );
}
