import { Typography } from '@mui/material';
import { formatDate } from '@/shared/lib/format';

/**
 * From which day the ledger is stored for the account (whatever the filters say): the ledger's costs count in the net returns
 * from this day on, and "Read from an earlier date" on the Portfolio screen can move it back as far as Dhan's history goes.
 */
export function LedgerCoverage({ coverageFrom }: { coverageFrom: string | null | undefined }) {
  return (
    <Typography variant="body2" color="text.secondary" role="status">
      {coverageFrom
        ? `Ledger read from ${formatDate(coverageFrom)}. Costs before that day are not counted.`
        : 'No ledger is stored for this account yet, so its costs are not counted.'}
    </Typography>
  );
}
