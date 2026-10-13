import {
  Alert,
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
import type { Trade, TradePage } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import type { InfiniteRows } from '@/shared/lib/useInfiniteRows';
import { LoadingState } from '@/shared/ui';
import { problemText } from '../../lib/errors';
import { TradeRow } from './TradeRow';

const headings = [
  { label: 'Date', align: 'left' },
  { label: 'Side', align: 'left' },
  { label: 'Qty', align: 'right' },
  { label: 'Price', align: 'right' },
  { label: 'Charges', align: 'right' },
  { label: 'Source', align: 'left' },
  { label: 'Action', align: 'right' },
] as const;

/** The trades as a table with Show more, or the loading, error and empty state. */
export function TradeList({
  list,
  holdingName,
  onVoid,
}: {
  list: InfiniteRows<Trade, TradePage>;
  holdingName: string;
  onVoid: (trade: Trade) => void;
}) {
  if (list.isPending) return <LoadingState label="Loading trades" />;
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
        {problemText(list.error, 'The trades could not be loaded.')}
      </Alert>
    );
  }
  if (list.rows.length === 0) {
    return (
      <Typography color="text.secondary">
        No trades for this holding yet. Import a tradebook or add a trade; the lots keep their dates until you rebuild
        them.
      </Typography>
    );
  }
  return (
    <>
      <TableContainer role="region" aria-label={`Trades of ${holdingName}, scrolls sideways`} tabIndex={0}>
        <Table size="small" aria-label={`Trades of ${holdingName}`} sx={{ minWidth: 560 }}>
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
            {list.rows.map((trade) => (
              <TradeRow key={trade.id} trade={trade} onVoid={onVoid} />
            ))}
          </TableBody>
        </Table>
      </TableContainer>
      <Stack spacing={0.5} sx={{ alignItems: 'flex-start', mt: 1 }}>
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
            {problemText(list.loadMoreError, 'The next trades could not be loaded.')}
          </Alert>
        )}
        {list.hasMore && !list.loadMoreError && (
          <Button size="small" loading={list.isFetchingNextPage} onClick={list.loadMore}>
            Show more
          </Button>
        )}
      </Stack>
    </>
  );
}
