import { Alert } from '@mui/material';
import { formatDate } from '@/shared/lib/format';

/**
 * Shown (never blocking) when an ex-date is on or before the date an instrument's CSV files reach: those files are
 * already adjusted at the source, so the action would adjust the same bars a second time.
 */
export function CsvAdjustedWarning({ through }: { through: string }) {
  return (
    <Alert severity="warning" role="note">
      This ex-date is on or before {formatDate(through)}, the last day of this instrument’s CSV files. Those files are
      already adjusted at the source, so adjusted prices before this date would be adjusted twice.
    </Alert>
  );
}
