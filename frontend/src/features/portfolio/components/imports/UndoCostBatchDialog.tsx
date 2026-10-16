import { Alert, Button, Typography } from '@mui/material';
import { useRef } from 'react';
import type { CostUndoResult } from '@/shared/api';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { useUndoCostBatch } from '../../api/reportMutations';
import { problemText } from '../../lib/errors';
import { countOf } from '../../lib/tradeText';

interface UndoCostBatchDialogProps {
  /** The import to undo: its file name and how many of its charges still count. */
  batch: { id: string; fileName: string; activeEntries: number };
  onUndone: (result: CostUndoResult) => void;
  onClose: () => void;
}

/** Asks before an import of depository charges is undone: its charges are voided (kept, flagged) and the sheet can be imported again. */
export function UndoCostBatchDialog({ batch, onUndone, onClose }: UndoCostBatchDialogProps) {
  const undo = useUndoCostBatch();
  const cancelRef = useRef<HTMLButtonElement>(null);
  return (
    <AppDialog open onClose={onClose} busy={undo.isPending} maxWidth="xs" initialFocusRef={cancelRef}>
      <AppDialogHeader subtitle={batch.fileName}>Undo this import?</AppDialogHeader>
      <AppDialogBody>
        <Typography>
          {`${countOf(batch.activeEntries, 'charge')} of “${batch.fileName}” will be voided: kept in the ledger list, flagged, and no longer counted in your net returns. The sheet can be imported again.`}
        </Typography>
        {undo.isError && (
          <Alert severity="error" role="alert" sx={{ mt: 1.5 }}>
            {problemText(undo.error, 'The import could not be undone. Nothing was changed.')}
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
