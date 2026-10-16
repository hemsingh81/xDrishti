import { Button } from '@mui/material';
import { useState } from 'react';
import type { TradeBatch } from '@/shared/api';
import { useToast } from '@/shared/ui/toastContext';
import { useUndoBatch } from '../../api/tradeMutations';
import { undoneText } from '../../lib/tradeText';
import { UndoBatchDialog } from '../trades/UndoBatchDialog';

/** "Undo this import" for a stored trade file: the trade import's own dry run and dialog, then the toast. */
export function UndoTradeImport({ batch, onUndone }: { batch: TradeBatch; onUndone: () => void }) {
  const { notify } = useToast();
  const undo = useUndoBatch();
  const [asking, setAsking] = useState(false);

  function close() {
    setAsking(false);
    undo.reset();
  }

  return (
    <>
      <Button
        variant="outlined"
        color="error"
        size="small"
        aria-label={`Undo the import of ${batch.fileName}`}
        onClick={() => {
          setAsking(true);
        }}
      >
        Undo this import
      </Button>
      {asking && (
        <UndoBatchDialog
          batch={batch}
          busy={undo.isPending}
          error={undo.isError ? undo.error : null}
          onCancel={close}
          onConfirm={() => {
            undo.mutate(batch.id, {
              onSuccess: (result) => {
                notify(undoneText(result));
                close();
                onUndone();
              },
            });
          }}
        />
      )}
    </>
  );
}
