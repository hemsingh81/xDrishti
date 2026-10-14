import { Box } from '@mui/material';
import type { LedgerClassTotal } from '@/shared/api';
import { StatTile } from '@/shared/ui';
import { amountDirection, classLabel, signedPrecise, totalsOf } from '../../lib/ledger';
import { countOf } from '../../lib/tradeText';

const toneOf = { up: 'good', down: 'bad', flat: 'default' } as const;

/** The five classes with their signed total and entry count for the period (whatever class the list is filtered to). */
export function LedgerTotals({ totals }: { totals: readonly LedgerClassTotal[] }) {
  return (
    <Box
      component="section"
      aria-label="Totals by class"
      sx={{ display: 'grid', gap: 1.5, gridTemplateColumns: 'repeat(auto-fit, minmax(150px, 1fr))' }}
    >
      {totalsOf(totals).map((total) => (
        <StatTile
          key={total.class}
          label={classLabel(total.class)}
          value={signedPrecise(total.amount)}
          hint={countOf(total.count, 'entry', 'entries')}
          tone={toneOf[amountDirection(total.amount)]}
        />
      ))}
    </Box>
  );
}
