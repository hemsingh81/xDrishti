import { Alert, Typography } from '@mui/material';
import { formatDate } from '@/shared/lib/format';
import { ConfirmDialog } from '@/shared/ui/ConfirmDialog';
import { csvAdjustedCounts, problemText } from '../lib/errors';

interface CsvAdjustedConfirmProps {
  open: boolean;
  /** The instrument the action is for; null when it is not known by name. */
  symbol: string | null;
  /** The day the instrument's CSV files reach, when the list gave it; else the server's own sentence is shown. */
  through: string | null;
  exDate: string;
  /** The 409 `data.csv_already_adjusted` answer (its counts and message). */
  error: unknown;
  busy?: boolean;
  /** Another failure of the repeated request, shown in the dialog. */
  failure?: string | null;
  onConfirm: () => void;
  onCancel: () => void;
}

/**
 * The API refused an action that would adjust history twice (409 `data.csv_already_adjusted`). The owner may say they
 * know ("Adjust anyway": the request is repeated with `acknowledgeCsvAdjusted`) or leave it out.
 */
export function CsvAdjustedConfirm({
  open,
  symbol,
  through,
  exDate,
  error,
  busy = false,
  failure = null,
  onConfirm,
  onCancel,
}: CsvAdjustedConfirmProps) {
  const name = symbol ?? 'this instrument';
  const { instruments } = csvAdjustedCounts(error);
  return (
    <ConfirmDialog
      open={open}
      title="Adjust history that is already adjusted?"
      confirmLabel="Adjust anyway"
      busy={busy}
      onConfirm={onConfirm}
      onCancel={onCancel}
    >
      {through ? (
        <Typography sx={{ mb: 1.5 }}>
          Bars of {name} up to {formatDate(through)} came from CSV files that are already adjusted for corporate
          actions. The ex-date {exDate === '' ? '' : `${formatDate(exDate)} `}is on or before that day, so adjusted
          prices before it would be adjusted twice.
        </Typography>
      ) : (
        <Typography sx={{ mb: 1.5 }}>
          {symbol ? `${symbol}: ` : ''}
          {problemText(error, 'This ex-date is inside CSV history that is already adjusted.')}
        </Typography>
      )}
      {instruments !== null && instruments > 1 && (
        <Typography sx={{ mb: 1.5 }}>{instruments} instruments are affected.</Typography>
      )}
      <Typography>
        Adjust anyway only if you know the CSV files of {name} were not adjusted for this action. Cancel leaves it out.
      </Typography>
      {failure && (
        <Alert severity="error" sx={{ mt: 2 }}>
          {failure}
        </Alert>
      )}
    </ConfirmDialog>
  );
}
