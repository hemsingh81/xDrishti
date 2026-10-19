import { Button, Divider, Stack, Typography } from '@mui/material';
import { useRef, useState } from 'react';
import type { DividendBatch } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { useInfiniteRows } from '@/shared/lib/useInfiniteRows';
import { LoadingState } from '@/shared/ui';
import { useToast } from '@/shared/ui/toastContext';
import { dividendBatchesList } from '../../api/dividendQueries';
import { undoneText } from '../../lib/dividendImport';
import { DividendBatchRow } from './DividendBatchRow';
import { UndoDividendDialog } from './UndoDividendDialog';
import { RetryAlert } from '../RetryAlert';

/** The imported dividend files of the account, newest first (50 at a time), each with Undo while it counts. */
export function DividendImportHistory({ accountId }: { accountId: string }) {
  const { notify } = useToast();
  const list = useInfiniteRows(dividendBatchesList(accountId));
  const [target, setTarget] = useState<DividendBatch | null>(null);
  const listRef = useRef<HTMLUListElement>(null);

  if (list.isPending) return <LoadingState label="Loading the imports" />;
  if (list.isError) {
    return <RetryAlert error={list.error} fallback="The imports could not be loaded." onRetry={list.refetch} />;
  }
  if (list.rows.length === 0) {
    return (
      <Typography color="text.secondary" sx={{ py: 3 }}>
        No dividend file has been imported for this account yet. Every import is listed here and can be undone.
      </Typography>
    );
  }
  return (
    <>
      <Stack
        component="ul"
        ref={listRef}
        tabIndex={-1}
        aria-label="Dividend imports"
        divider={<Divider component="li" />}
        sx={{ m: 0, p: 0, listStyle: 'none' }}
      >
        {list.rows.map((batch) => (
          <DividendBatchRow key={batch.id} batch={batch} onUndo={setTarget} />
        ))}
      </Stack>
      <Stack spacing={0.5} sx={{ alignItems: 'flex-start', mt: 1.5 }}>
        <Typography variant="caption" color="text.secondary" role="status" className="numeric">
          {`Showing ${formatNumber(list.rows.length)} of ${formatNumber(list.total)}`}
        </Typography>
        {list.loadMoreError && (
          <RetryAlert
            error={list.loadMoreError}
            fallback="The next imports could not be loaded."
            severity="warning"
            onRetry={list.retryMore}
          />
        )}
        {list.hasMore && !list.loadMoreError && (
          <Button size="small" loading={list.isFetchingNextPage} onClick={list.loadMore}>
            Show more
          </Button>
        )}
      </Stack>
      {target && (
        <UndoDividendDialog
          batch={target}
          onClose={() => {
            setTarget(null);
          }}
          onUndone={(result) => {
            notify(undoneText(result));
            setTarget(null);
            // The Undo button that opened the dialog is gone: the keyboard goes to the list.
            requestAnimationFrame(() => {
              listRef.current?.focus();
            });
          }}
        />
      )}
    </>
  );
}
