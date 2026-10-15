import { Alert, Button, Divider, Stack, Typography } from '@mui/material';
import { useRef, useState } from 'react';
import type { CostBatch, TradeBatch } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { useInfiniteRows } from '@/shared/lib/useInfiniteRows';
import { LoadingState } from '@/shared/ui';
import { useToast } from '@/shared/ui/toastContext';
import { costBatchesList } from '../../api/costQueries';
import { useUndoBatch } from '../../api/tradeMutations';
import { tradeBatchesList } from '../../api/tradeQueries';
import { problemText } from '../../lib/errors';
import { costUndoneText } from '../../lib/reportImport';
import { undoneText } from '../../lib/tradeText';
import { CostBatchRow } from '../imports/CostBatchRow';
import { UndoCostBatchDialog } from '../imports/UndoCostBatchDialog';
import { BatchRow } from './BatchRow';
import { UndoBatchDialog } from './UndoBatchDialog';

type Entry = { kind: 'trades'; batch: TradeBatch } | { kind: 'costs'; batch: CostBatch };

/**
 * The imported files, newest first (50 of each kind at a time): tradebooks and transaction reports (kind Trades) and
 * depository-charges sheets (kind DP charges), each with Undo while it counts.
 */
export function ImportHistory() {
  const { notify } = useToast();
  const trades = useInfiniteRows(tradeBatchesList);
  const costs = useInfiniteRows(costBatchesList);
  const undo = useUndoBatch();
  const [target, setTarget] = useState<Entry | null>(null);
  const listRef = useRef<HTMLUListElement>(null);

  function close() {
    setTarget(null);
    undo.reset();
  }

  // The focus goes to the list once the Undo button that opened the dialog is gone (the import is undone).
  const focusList = () => {
    requestAnimationFrame(() => {
      listRef.current?.focus();
    });
  };

  function confirm() {
    if (target?.kind !== 'trades') return;
    undo.mutate(target.batch.id, {
      onSuccess: (result) => {
        notify(undoneText(result));
        close();
        focusList();
      },
    });
  }

  if (trades.isPending || costs.isPending) return <LoadingState label="Loading the imports" />;
  const failed = trades.isError ? trades : costs.isError ? costs : null;
  if (failed) {
    return (
      <Alert
        severity="error"
        action={
          <Button
            color="inherit"
            size="small"
            onClick={() => {
              trades.refetch();
              costs.refetch();
            }}
          >
            Retry
          </Button>
        }
      >
        {problemText(failed.error, 'The imports could not be loaded.')}
      </Alert>
    );
  }
  const entries: Entry[] = [
    ...trades.rows.map((batch): Entry => ({ kind: 'trades', batch })),
    ...costs.rows.map((batch): Entry => ({ kind: 'costs', batch })),
  ].sort((a, b) => b.batch.createdAt.localeCompare(a.batch.createdAt));
  if (entries.length === 0) {
    return (
      <Typography color="text.secondary" sx={{ py: 3 }}>
        No tradebook has been imported yet. Use Import CSV to load one; every import is listed here and can be undone.
        Depository-charges sheets imported with Import files are listed here too.
      </Typography>
    );
  }
  const loadMoreError = trades.loadMoreError ?? costs.loadMoreError;
  const hasMore = trades.hasMore || costs.hasMore;
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
        {entries.map((entry) =>
          entry.kind === 'trades' ? (
            <BatchRow
              key={entry.batch.id}
              batch={entry.batch}
              onUndo={(batch) => {
                setTarget({ kind: 'trades', batch });
              }}
            />
          ) : (
            <CostBatchRow
              key={entry.batch.id}
              batch={entry.batch}
              onUndo={(batch) => {
                setTarget({ kind: 'costs', batch });
              }}
            />
          ),
        )}
      </Stack>
      <Stack spacing={0.5} sx={{ alignItems: 'flex-start', mt: 1.5 }}>
        <Typography variant="caption" color="text.secondary" role="status" className="numeric">
          {`Showing ${formatNumber(entries.length)} of ${formatNumber(trades.total + costs.total)}`}
        </Typography>
        {loadMoreError && (
          <Alert
            severity="warning"
            action={
              <Button
                color="inherit"
                size="small"
                onClick={() => {
                  if (trades.loadMoreError) trades.retryMore();
                  if (costs.loadMoreError) costs.retryMore();
                }}
              >
                Retry
              </Button>
            }
          >
            {problemText(loadMoreError, 'The next imports could not be loaded.')}
          </Alert>
        )}
        {hasMore && !loadMoreError && (
          <Button
            size="small"
            loading={trades.isFetchingNextPage || costs.isFetchingNextPage}
            onClick={() => {
              trades.loadMore();
              costs.loadMore();
            }}
          >
            Show more
          </Button>
        )}
      </Stack>
      {target?.kind === 'trades' && (
        <UndoBatchDialog
          batch={target.batch}
          busy={undo.isPending}
          error={undo.isError ? undo.error : null}
          onConfirm={confirm}
          onCancel={close}
        />
      )}
      {target?.kind === 'costs' && (
        <UndoCostBatchDialog
          batch={target.batch}
          onClose={close}
          onUndone={(result) => {
            notify(costUndoneText(result));
            close();
            focusList();
          }}
        />
      )}
    </>
  );
}
