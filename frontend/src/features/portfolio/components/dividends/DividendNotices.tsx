import { Alert, Stack } from '@mui/material';
import type { DividendPreviewReport } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { BadRows } from '../trades/BadRows';
import { BeforeFirstLot } from './BeforeFirstLot';
import { IdenticalRepeats } from './IdenticalRepeats';

/** What the owner should know before importing: unreadable rows, repeats, amounts that do not add up, dividends before the first lot. */
export function DividendNotices({
  report,
  account,
}: {
  report: DividendPreviewReport;
  account: { id: string; name: string };
}) {
  const mismatches = report.items.filter((item) => item.warnings.includes('amount_mismatch')).length;
  return (
    <Stack spacing={1.5}>
      <BadRows rows={report.file.bad} total={report.file.badTotal} />
      <IdenticalRepeats repeats={report.file.identicalRepeats} />
      {mismatches > 0 && (
        <Alert severity="info">
          {`${formatNumber(mismatches)} ${mismatches === 1 ? 'row has' : 'rows have'} a per-share amount times shares that differs from the amount paid. The amount paid is used.`}
        </Alert>
      )}
      <BeforeFirstLot account={account} before={report.beforeFirstLot} />
    </Stack>
  );
}
