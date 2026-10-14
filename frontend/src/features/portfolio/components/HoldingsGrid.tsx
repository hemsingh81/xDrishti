import { Button, Card, Stack, Typography } from '@mui/material';
import type { ReactNode } from 'react';
import type { HoldingRow } from '@/shared/api';
import type { InfiniteRows } from '@/shared/lib/useInfiniteRows';
import { Grid, type GridSort } from '@/shared/ui/grid';
import { useLiveRows } from '../hooks/liveContext';
import { problemText } from '../lib/errors';
import { holdingsParams } from '../api/portfolioQueries';
import { isSortField, type PortfolioFilters } from '../lib/filters';
import { holdingColumns, holdingColumnsWithAccount } from './holdingColumns';

interface HoldingsGridProps {
  result: InfiniteRows<HoldingRow>;
  filters: PortfolioFilters;
  openId: string | null;
  /** More than one account's holdings are listed: show the account column. */
  showAccount: boolean;
  /** Shown when the list is empty. */
  emptyState: ReactNode;
  onChange: (patch: Partial<PortfolioFilters>) => void;
  onOpen: (id: string) => void;
}

const getRowId = (holding: HoldingRow) => holding.id;
const getRowLabel = (holding: HoldingRow) => holding.symbol;

/** The holdings card: fills the space under the tiles; the rows scroll inside it between the header and the footer
 * and further pages load as the end comes into view. */
export function HoldingsGrid({
  result,
  filters,
  openId,
  showAccount,
  emptyState,
  onChange,
  onOpen,
}: HoldingsGridProps) {
  const { isPending, isError, isPlaceholderData, error, loadMoreError } = result;
  // Rows at the prices the hub pushed since the last read; the order stays the read's (rows do not jump while it is read).
  const rows = useLiveRows(result.rows);
  const sort: GridSort = { columnId: filters.sort.field, descending: filters.sort.descending };

  return (
    <Card sx={{ display: 'flex', flexDirection: 'column', flex: '1 1 auto', minHeight: 320 }}>
      <Grid
        label="Holdings"
        columns={showAccount ? holdingColumnsWithAccount : holdingColumns}
        rows={rows}
        getRowId={getRowId}
        getRowLabel={getRowLabel}
        sort={sort}
        onSortChange={(next) => {
          if (isSortField(next.columnId)) onChange({ sort: { field: next.columnId, descending: next.descending } });
        }}
        onRowClick={(holding) => {
          onOpen(holding.id);
        }}
        activeRowId={openId}
        loading={isPending || isPlaceholderData}
        error={isError ? problemText(error, 'The holdings could not be loaded.') : null}
        onRetry={result.refetch}
        resetKey={JSON.stringify(holdingsParams(filters))}
        infinite={{
          total: result.total,
          hasMore: result.hasMore,
          loadingMore: result.isFetchingNextPage,
          onLoadMore: result.loadMore,
          error: loadMoreError ? problemText(loadMoreError, 'The next holdings could not be loaded.') : null,
          onRetryMore: result.retryMore,
        }}
        emptyState={emptyState}
      />
    </Card>
  );
}

/** "No holdings match these filters" with a way to clear them. */
export function NoMatches({ canClear, onClear }: { canClear: boolean; onClear: () => void }) {
  return (
    <Stack spacing={1} sx={{ alignItems: 'center', py: 5 }}>
      <Typography sx={{ fontWeight: 600 }}>No holdings match these filters</Typography>
      {canClear && <Button onClick={onClear}>Clear filters</Button>}
    </Stack>
  );
}
