import { Box, Typography } from '@mui/material';
import type { TradeBadRow } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { countOf } from '../../lib/tradeText';
import { Disclosure } from './Disclosure';

/** The rows that could not be read, with their line numbers, behind a button; at most 200 are listed by the API. */
export function BadRows({ rows, total }: { rows: readonly TradeBadRow[]; total: number }) {
  if (total === 0) return null;
  return (
    <Disclosure
      label={`Show ${countOf(total, 'row')} that could not be read`}
      openLabel="Hide the rows that could not be read"
    >
      <Typography variant="body2" color="text.secondary" sx={{ mb: 0.5 }}>
        These rows are never imported. Correct them in the file and preview again.
      </Typography>
      <Box
        component="ul"
        aria-label="Rows that could not be read"
        sx={{ m: 0, pl: 3, maxHeight: 220, overflowY: 'auto' }}
      >
        {rows.map((row) => (
          <li key={row.line}>
            <span className="numeric">Line {row.line}</span>: {row.reason}
          </li>
        ))}
      </Box>
      {total > rows.length && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>
          {`Showing the first ${formatNumber(rows.length)} of ${formatNumber(total)}.`}
        </Typography>
      )}
    </Disclosure>
  );
}
