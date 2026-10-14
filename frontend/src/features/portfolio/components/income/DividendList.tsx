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
import type { DividendPage, DividendView } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import type { InfiniteRows } from '@/shared/lib/useInfiniteRows';
import { LoadingState } from '@/shared/ui';
import { problemText } from '../../lib/errors';
import { DividendRow } from './DividendRow';

interface DividendListProps {
  list: InfiniteRows<DividendView, DividendPage>;
  /** What the table is called: "RELIANCE" or "Test Dhan Main". */
  owner: string;
  /** Also show which holding each dividend is for (the account's list). */
  showHolding: boolean;
  /** Said when nothing is entered. */
  emptyText: string;
  onVoid: (dividend: DividendView) => void;
}

/** The dividends as a table with Show more, or the loading, error and empty state. */
export function DividendList({ list, owner, showHolding, emptyText, onVoid }: DividendListProps) {
  const headings = [
    { label: 'Pay date', align: 'left' },
    ...(showHolding ? [{ label: 'Holding', align: 'left' } as const] : []),
    { label: 'Amount', align: 'right' },
    { label: 'Per share', align: 'right' },
    { label: 'Note', align: 'left' },
    { label: 'Action', align: 'right' },
  ] as const;
  if (list.isPending) return <LoadingState label="Loading dividends" />;
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
        {problemText(list.error, 'The dividends could not be loaded.')}
      </Alert>
    );
  }
  if (list.rows.length === 0) return <Typography color="text.secondary">{emptyText}</Typography>;
  return (
    <>
      <TableContainer role="region" aria-label={`Dividends of ${owner}, scrolls sideways`} tabIndex={0}>
        <Table size="small" aria-label={`Dividends of ${owner}`} sx={{ minWidth: showHolding ? 640 : 520 }}>
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
            {list.rows.map((dividend) => (
              <DividendRow key={dividend.id} dividend={dividend} showHolding={showHolding} onVoid={onVoid} />
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
            {problemText(list.loadMoreError, 'The next dividends could not be loaded.')}
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
