import { Button } from '@mui/material';
import { useState } from 'react';
import type { CostBatch } from '@/shared/api';
import { useToast } from '@/shared/ui/toastContext';
import { costUndoneText } from '../../lib/reportImport';
import { UndoCostBatchDialog } from './UndoCostBatchDialog';

/** "Undo this import" for a stored depository-charges sheet: asks, voids its charges and says so in a toast. */
export function UndoCostImport({ batch, onUndone }: { batch: CostBatch; onUndone: () => void }) {
  const { notify } = useToast();
  const [asking, setAsking] = useState(false);
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
        <UndoCostBatchDialog
          batch={batch}
          onClose={() => {
            setAsking(false);
          }}
          onUndone={(result) => {
            notify(costUndoneText(result));
            setAsking(false);
            onUndone();
          }}
        />
      )}
    </>
  );
}
