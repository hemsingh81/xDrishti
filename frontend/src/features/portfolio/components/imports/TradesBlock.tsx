import { Stack, Typography } from '@mui/material';
import type { TradePreviewReport } from '@/shared/api';
import { BadRows } from '../trades/BadRows';
import { HeldNotes } from '../trades/HeldNotes';
import { SecuritiesTable } from '../trades/SecuritiesTable';
import { SkippedReasons } from './SkippedReasons';

/**
 * What importing the trades would do: the rows left out (with the reasons), the rows that could not be read, and each security
 * with the result of rebuilding its holding's lots (the trade import's own table and notes).
 */
export function TradesBlock({ report }: { report: TradePreviewReport }) {
  const matched = report.securities.filter((security) => security.matched);
  const unmatched = report.securities.filter((security) => !security.matched);
  return (
    <Stack spacing={1.5}>
      <SkippedReasons skipped={report.file.skipped} />
      <BadRows rows={report.file.bad} total={report.file.badTotal} />
      <Typography component="h5" variant="h4">
        Securities
      </Typography>
      <SecuritiesTable securities={matched} />
      <HeldNotes report={report} unmatched={unmatched} />
    </Stack>
  );
}
