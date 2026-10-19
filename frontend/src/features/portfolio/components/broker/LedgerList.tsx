import {
  Button,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import { useEffect, useRef } from 'react';
import type { LedgerEntry, LedgerPage } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import type { InfiniteRows } from '@/shared/lib/useInfiniteRows';
import { LoadingState } from '@/shared/ui';
import { LedgerRow } from './LedgerRow';
import { RetryAlert } from '../RetryAlert';

const headings = [
  { label: 'Date', align: 'left' },
  { label: 'Details', align: 'left' },
  { label: 'Class', align: 'left' },
  { label: 'Amount', align: 'right' },
  { label: 'Balance', align: 'right' },
] as const;

interface LedgerListProps {
  list: InfiniteRows<LedgerEntry, LedgerPage>;
  /** Something narrows the list (a class or a period): the empty state then says that nothing matches. */
  filtered: boolean;
}

/** The ledger entries as a table with Show more, or the loading, error and empty state. */
export function LedgerList({ list, filtered }: LedgerListProps) {
  const region = useRef<HTMLDivElement>(null);
  const pressedMore = useRef(false);
  const exhausted = !list.hasMore && !list.isFetchingNextPage;
  // Show more goes away with the last page: the keyboard then goes to the table, not to the page's start.
  useEffect(() => {
    if (exhausted && pressedMore.current) {
      pressedMore.current = false;
      region.current?.focus();
    }
  }, [exhausted]);
  if (list.isPending) return <LoadingState label="Loading the ledger" />;
  if (list.isError) {
    return <RetryAlert error={list.error} fallback="The ledger could not be loaded." onRetry={list.refetch} />;
  }
  if (list.rows.length === 0) {
    return (
      <Typography color="text.secondary" sx={{ py: 2 }}>
        {filtered
          ? 'No ledger entries match this class and period.'
          : 'No ledger entries yet. They arrive with the nightly read of the account, or press Sync trades & ledger on the Portfolio screen.'}
      </Typography>
    );
  }
  return (
    <>
      <TableContainer ref={region} role="region" aria-label="Ledger entries, scrolls sideways" tabIndex={0}>
        <Table size="small" aria-label="Ledger entries" sx={{ minWidth: 600 }}>
          <TableHead>
            <TableRow>
              {headings.map((heading) => (
                <TableCell key={heading.label} sx={{ fontWeight: 600, textAlign: heading.align }}>
                  {heading.label}
                </TableCell>
              ))}
            </TableRow>
          </TableHead>
          <TableBody>
            {list.rows.map((entry) => (
              <LedgerRow key={entry.id} entry={entry} />
            ))}
          </TableBody>
        </Table>
      </TableContainer>
      <Stack spacing={0.5} sx={{ alignItems: 'flex-start', mt: 1 }}>
        <Typography variant="caption" color="text.secondary" role="status" className="numeric">
          {`Showing ${formatNumber(list.rows.length)} of ${formatNumber(list.total)}`}
        </Typography>
        {list.loadMoreError && (
          <RetryAlert
            error={list.loadMoreError}
            fallback="The next entries could not be loaded."
            severity="warning"
            onRetry={list.retryMore}
          />
        )}
        {list.hasMore && !list.loadMoreError && (
          // Not `loading`: that disables the button and the keyboard would lose its place while the page loads.
          <Button
            size="small"
            aria-disabled={list.isFetchingNextPage}
            onClick={() => {
              pressedMore.current = true;
              list.loadMore();
            }}
          >
            {list.isFetchingNextPage ? 'Loading…' : 'Show more'}
          </Button>
        )}
      </Stack>
    </>
  );
}
