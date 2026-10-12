import { Typography } from '@mui/material';
import type { SpikeCheck } from '@/shared/api';
import { formatIstDateTime, formatNumber, formatPercent } from '@/shared/lib/format';
import { checkTitles } from '../../lib/labels';
import { CheckSection } from './CheckSection';
import { MiniTable } from './MiniTable';

const signed = (value: number) => `${value > 0 ? '+' : ''}${formatPercent(value)} %`;

/** Spikes: a one-minute move far above the median that the next bar takes back is a bad tick; one it keeps is a jump. */
export function SpikesSection({ check }: { check: SpikeCheck }) {
  return (
    <CheckSection title={checkTitles.spikes} status={check.status}>
      <Typography>
        {formatNumber(check.spikes)} {check.spikes === 1 ? 'spike' : 'spikes'} and {formatNumber(check.largeMoves)}{' '}
        large {check.largeMoves === 1 ? 'move' : 'moves'}. The median one-minute move is{' '}
        {formatPercent(check.medianMovePercent)} %; a bar needs a move above {formatPercent(check.thresholdPercent)} %
        to count.
      </Typography>
      <Typography variant="body2" color="text.secondary">
        A spike (a warning) is a move the next bar takes back; a large move stays and is only reported.
      </Typography>
      {check.examples.length > 0 && (
        <MiniTable
          label="Spike examples"
          columns={[{ header: 'Bar (IST)' }, { header: 'Move', numeric: true }, { header: 'Next bar' }]}
          rows={check.examples.map((e) => ({
            key: e.ts,
            cells: [
              <time key="t" dateTime={e.ts}>
                {formatIstDateTime(e.ts)}
              </time>,
              signed(e.movePercent),
              e.reversed ? 'Takes it back (reversed: a bad tick)' : 'Keeps it (a real move)',
            ],
          }))}
        />
      )}
    </CheckSection>
  );
}
