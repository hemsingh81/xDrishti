import { Box, Typography } from '@mui/material';
import { useInfiniteQuery } from '@tanstack/react-query';
import { useNavigate } from 'react-router';
import { formatR } from '@/shared/analytics';
import { ApiError } from '@/shared/api';
import { formatDate } from '@/shared/lib/format';
import { Grid, type GridColumn } from '@/shared/ui/grid';
import { tradesInfiniteQuery, type TradeLine } from '../api/reportQueries';
import { queryString, type ReportQuery } from '../lib/reportFilter';
import { Panel } from './Panel';

const columns: GridColumn<TradeLine>[] = [
  { id: 'exit', header: 'Exit date', accessor: (t) => formatDate(t.exitDate), minWidth: 110 },
  { id: 'symbol', header: 'Symbol', accessor: (t) => t.symbol, minWidth: 110, grow: true },
  { id: 'strategy', header: 'Strategy', accessor: (t) => t.strategy, minWidth: 90, hideBelow: 'md' },
  { id: 'side', header: 'Side', accessor: (t) => t.side, minWidth: 60 },
  { id: 'timeframe', header: 'TF', accessor: (t) => t.timeframe, minWidth: 50, hideBelow: 'sm' },
  { id: 'r', header: 'R', accessor: (t) => formatR(t.r), numeric: true, align: 'right', minWidth: 80 },
  {
    id: 'sessions',
    header: 'Sessions',
    accessor: (t) => String(t.sessionsHeld),
    numeric: true,
    align: 'right',
    minWidth: 80,
    hideBelow: 'md',
  },
];

const rowId = (t: TradeLine) => `${t.runKey}:${String(t.ordinal)}`;

/** The closed trades of the cell, 50 per page (the very set the figures were computed from); a row opens the replay of the trade. */
export function TradeList({ query }: { query: ReportQuery }) {
  const navigate = useNavigate();
  const result = useInfiniteQuery(tradesInfiniteQuery(query));
  const rows = result.data?.pages.flatMap((page) => page.items) ?? [];
  const total = result.data?.pages[0]?.total ?? 0;
  const message = result.isError
    ? result.error instanceof ApiError || result.error instanceof Error
      ? result.error.message
      : 'Could not load.'
    : null;
  return (
    <Panel title="Trades of this cell">
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
        Open a trade to see it on its own bars. Simulated trades on replayed history.
      </Typography>
      <Box sx={{ height: 420 }}>
        <Grid
          label="Trades of this cell"
          columns={columns}
          rows={rows}
          getRowId={rowId}
          getRowLabel={(t) => `${t.symbol} ${t.side} ${t.exitDate}`}
          resetKey={queryString(query)}
          loading={result.isPending}
          error={message}
          onRetry={() => {
            void result.refetch();
          }}
          emptyState="No closed trades in this cell."
          onRowClick={(t) => {
            void navigate(`/reports/trades/${t.runKey}/${String(t.ordinal)}`);
          }}
          infinite={{
            total,
            hasMore: result.hasNextPage,
            loadingMore: result.isFetchingNextPage,
            onLoadMore: () => {
              void result.fetchNextPage();
            },
          }}
        />
      </Box>
    </Panel>
  );
}
