import { Alert, Box, Typography } from '@mui/material';
import type { CsvFileRow } from '@/shared/api';
import { formatIstDateTime, formatNumber } from '@/shared/lib/format';
import { ConfirmDialog } from '@/shared/ui/ConfirmDialog';
import { useUndoCsvLoad } from '../api/csvMutations';
import { csvErrorText } from '../lib/errors';

interface UndoCsvDialogProps {
  file: CsvFileRow;
  onClose: () => void;
  /** The undo job was accepted: the page follows it. */
  onStarted: () => void;
}

function rangeText(file: CsvFileRow): string {
  return file.firstBar && file.lastBar
    ? `from ${formatIstDateTime(file.firstBar)} to ${formatIstDateTime(file.lastBar)} (IST)`
    : `over the whole range of the instrument (the range of this file was not recorded)`;
}

/**
 * "Undo the load of FILE?": says exactly what is removed and what is not, before anything happens. The removal and the
 * rebuild of the aggregates run as a data job that the page follows.
 */
export function UndoCsvDialog({ file, onClose, onStarted }: UndoCsvDialogProps) {
  const undo = useUndoCsvLoad();
  const instrument = file.instrument?.symbol ?? file.symbol ?? 'its instrument';
  const overlaps = file.overlapsWith ?? [];

  const confirm = async () => {
    try {
      await undo.mutateAsync(file.name);
      onStarted();
    } catch {
      // The dialog shows the reason and stays open.
    }
  };

  return (
    <ConfirmDialog
      open
      title={`Undo the load of ${file.name}?`}
      confirmLabel="Undo load"
      destructive
      busy={undo.isPending}
      onConfirm={() => {
        void confirm();
      }}
      onCancel={onClose}
    >
      <Typography component="div">
        This removes the CSV bars of {instrument} {rangeText(file)}
        {file.rows === null ? '' : `, the ${formatNumber(file.rows)} rows this file loaded`}.
      </Typography>
      <Box component="ul" aria-label="What the undo does" sx={{ mt: 1, mb: 0, pl: 2.5 }}>
        <li>Bars that came from Dhan stay.</li>
        <li>The aggregates of {instrument} are rebuilt afterwards.</li>
        <li>The file becomes pending again, so it can be loaded again.</li>
        <li>Values that an earlier load overwrote are not restored.</li>
        <li>{instrument} stays tracked.</li>
      </Box>
      {overlaps.length > 0 && (
        <Alert severity="warning" sx={{ mt: 2 }}>
          Undoing this file also removes the CSV bars of: {overlaps.join(', ')}. Undo removes the instrument’s CSV bars
          in this file’s time range, whichever file loaded them.
        </Alert>
      )}
      {undo.isError && (
        <Alert severity="error" sx={{ mt: 2 }}>
          {csvErrorText(undo.error, 'The load could not be undone.')}
        </Alert>
      )}
    </ConfirmDialog>
  );
}
