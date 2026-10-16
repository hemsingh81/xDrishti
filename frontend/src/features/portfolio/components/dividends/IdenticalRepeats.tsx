import { Alert, Box } from '@mui/material';
import type { DividendRepeat } from '@/shared/api';
import { formatDate, formatInrPrecise, formatNumber } from '@/shared/lib/format';

/** Payments that appear more than once, identically, in the file: counted once, with the lines they are on. */
export function IdenticalRepeats({ repeats }: { repeats: readonly DividendRepeat[] }) {
  if (repeats.length === 0) return null;
  return (
    <Alert severity="info">
      The same payment appears more than once in the file. Each is counted once; add the second by hand if it is real.
      <Box
        component="ul"
        aria-label="Payments that repeat"
        sx={{ m: 0, mt: 0.5, pl: 3, maxHeight: 180, overflowY: 'auto' }}
      >
        {repeats.map((repeat) => (
          <li key={`${repeat.name}|${repeat.payDate}|${String(repeat.amount)}|${String(repeat.firstLine)}`}>
            {`${repeat.name}, ${formatDate(repeat.payDate)}, ${formatInrPrecise(repeat.amount)} appears ${formatNumber(repeat.occurrences)} times (line ${repeat.lines.map(formatNumber).join(', ')}) — counted once.`}
          </li>
        ))}
      </Box>
    </Alert>
  );
}
