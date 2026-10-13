import { Alert, Button, Divider, Stack, Typography } from '@mui/material';
import { useRef, useState } from 'react';
import type { TradeBatch } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { useInfiniteRows } from '@/shared/lib/useInfiniteRows';
import { LoadingState } from '@/shared/ui';
import { useToast } from '@/shared/ui/toastContext';
import { useUndoBatch } from '../../api/tradeMutations';
import { tradeBatchesList } from '../../api/tradeQueries';
import { problemText } from '../../lib/errors';
import { undoneText } from '../../lib/tradeText';
import { BatchRow } from './BatchRow';
import { UndoBatchDialog } from './UndoBatchDialog';

/** The imported files, newest first (50 at a time), each with Undo while it counts. */
export function ImportHistory() {
  const { notify } = useToast();
  const list = useInfiniteRows(tradeBatchesList);
  const undo = useUndoBatch();
  const [target, setTarget] = useState<TradeBatch | null>(null);
  const listRef = useRef<HTMLUListElement>(null);

  function close() {
    setTarget(null);
    undo.reset();
  }

  function confirm() {
    if (!target) return;
    undo.mutate(target.id, {
      onSuccess: (result) => {
        notify(undoneText(result));
        close();
        // The Undo button that opened the dialog is gone (the import is undone): the keyboard goes to the list.
        requestAnimationFrame(() => {
          listRef.current?.focus();
        });
      },
    });
  }

  if (list.isPending) return <LoadingState label="Loading the imports" />;
  if (list.isError) {
    return (
      <Alert
        severity="error"
        action={
          <Button color="inherit" size="small" onClick={list.refetch}>
            Retry
          </Button>
        }
      >
        {problemText(list.error, 'The imports could not be loaded.')}
      </Alert>
    );
  }
  if (list.rows.length === 0) {
    return (
      <Typography color="text.secondary" sx={{ py: 3 }}>
        No tradebook has been imported yet. Use Import CSV to load one; every import is listed here and can be undone.
      </Typography>
    );
  }
  return (
    <>
      <Stack
        component="ul"
        ref={listRef}
        tabIndex={-1}
        aria-label="Imports"
        divider={<Divider component="li" />}
        sx={{ m: 0, p: 0, listStyle: 'none' }}
      >
        {list.rows.map((batch) => (
          <BatchRow key={batch.id} batch={batch} onUndo={setTarget} />
        ))}
      </Stack>
      <Stack spacing={0.5} sx={{ alignItems: 'flex-start', mt: 1.5 }}>
        <Typography variant="caption" color="text.secondary" role="status" className="numeric">
          {`Showing ${formatNumber(list.rows.length)} of ${formatNumber(list.total)}`}
        </Typography>
        {list.loadMoreError && (
          <Alert
            severity="warning"
            action={
              <Button color="inherit" size="small" onClick={list.retryMore}>
                Retry
              </Button>
            }
          >
            {problemText(list.loadMoreError, 'The next imports could not be loaded.')}
          </Alert>
        )}
        {list.hasMore && !list.loadMoreError && (
          <Button size="small" loading={list.isFetchingNextPage} onClick={list.loadMore}>
            Show more
          </Button>
        )}
      </Stack>
      {target && (
        <UndoBatchDialog
          batch={target}
          busy={undo.isPending}
          error={undo.isError ? undo.error : null}
          onConfirm={confirm}
          onCancel={close}
        />
      )}
    </>
  );
}
