import { Alert, Button, Typography } from '@mui/material';
import { useRef } from 'react';
import type { DividendUndoResult } from '@/shared/api';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { useUndoDividendBatch } from '../../api/dividendMutations';
import { dividendErrorText } from '../../lib/dividendErrors';
import { countOf } from '../../lib/tradeText';

interface UndoDividendDialogProps {
  /** The import to undo: its file name and how many of its dividends still count. */
  batch: { id: string; fileName: string; activeDividends: number };
  onUndone: (result: DividendUndoResult) => void;
  onClose: () => void;
}

/** Asks before an import is undone: its dividends are voided (kept in the lists, flagged), and the file can be imported again. */
export function UndoDividendDialog({ batch, onUndone, onClose }: UndoDividendDialogProps) {
  const undo = useUndoDividendBatch();
  const cancelRef = useRef<HTMLButtonElement>(null);
  return (
    <AppDialog open onClose={onClose} busy={undo.isPending} maxWidth="xs" initialFocusRef={cancelRef}>
      <AppDialogHeader subtitle={batch.fileName}>Undo this import?</AppDialogHeader>
      <AppDialogBody>
        <Typography>
          {`${countOf(batch.activeDividends, 'dividend')} of “${batch.fileName}” will be voided: kept in the list, flagged, and no longer counted in your returns. The file can be imported again.`}
        </Typography>
        {undo.isError && (
          <Alert severity="error" role="alert" sx={{ mt: 1.5 }}>
            {dividendErrorText(undo.error, 'The import could not be undone. Nothing was changed.')}
          </Alert>
        )}
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={onClose} disabled={undo.isPending} ref={cancelRef}>
          Cancel
        </Button>
        <Button
          variant="contained"
          color="error"
          loading={undo.isPending}
          onClick={() => {
            undo.mutate(batch.id, { onSuccess: onUndone });
          }}
        >
          Undo import
        </Button>
      </AppDialogFooter>
    </AppDialog>
  );
}
