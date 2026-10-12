import { Box, Typography } from '@mui/material';
import type { SanityCheck } from '@/shared/api';
import { formatIstDateTime, formatNumber } from '@/shared/lib/format';
import { checkTitles } from '../../lib/labels';
import { CheckSection } from './CheckSection';

/** Bar sanity: low ≤ open and close ≤ high, positive prices, volume not negative. */
export function SanitySection({ check }: { check: SanityCheck }) {
  return (
    <CheckSection title={checkTitles.sanity} status={check.status}>
      <Typography>
        {formatNumber(check.barsChecked)} bars checked;{' '}
        {check.badBars === 0 ? 'none is' : `${formatNumber(check.badBars)} are`} bad. A bar is bad when its low is above
        its open or close, its high below them, a price is not positive, or the volume is negative.
      </Typography>
      {check.examples.length > 0 && (
        <Box component="ul" aria-label="Bad bars" sx={{ m: 0, pl: 2.5 }}>
          {check.examples.map((example) => (
            <li key={example.ts}>
              <Typography variant="body2">
                <time dateTime={example.ts}>{formatIstDateTime(example.ts)}</time>: {example.problem}
              </Typography>
            </li>
          ))}
        </Box>
      )}
    </CheckSection>
  );
}
