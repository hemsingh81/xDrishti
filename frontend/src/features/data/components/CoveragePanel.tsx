import { Card, Divider } from '@mui/material';
import type { BarsOverview, DataCoverageRow } from '@/shared/api';
import { useInfiniteRows } from '@/shared/lib/useInfiniteRows';
import { Grid, type GridSort } from '@/shared/ui/grid';
import { coverageList } from '../api/dataQueries';
import type { useCoverageFilters } from '../hooks/useCoverageFilters';
import { problemText } from '../lib/errors';
import { activeFilterCount, isSortField } from '../lib/filters';
import { coverageColumns } from './coverageColumns';
import { CoverageEmpty } from './CoverageEmpty';
import { CoverageToolbar } from './CoverageToolbar';

const getRowId = (row: DataCoverageRow) => row.id;

/**
 * What is stored per instrument: last bar (highlighted when behind), 1-minute bars, source, gaps and status, in a list
 * that fills the tab and loads further rows as it is scrolled. A row opens the bars drawer. The state is in the URL.
 */
export function CoveragePanel({
  filterState,
  bars,
}: {
  filterState: ReturnType<typeof useCoverageFilters>;
  bars: BarsOverview;
}) {
  const { filters, update, clear, openId, open } = filterState;
  const result = useInfiniteRows(coverageList(filters));
  const { rows, isPending, isError, isPlaceholderData, error, loadMoreError } = result;
  const sort: GridSort = { columnId: filters.sort.field, descending: filters.sort.descending };

  return (
    <Card sx={{ display: 'flex', flexDirection: 'column', flex: '1 1 auto', minHeight: 0 }}>
      <CoverageToolbar filters={filters} bars={bars} onChange={update} onClear={clear} />
      <Divider />
      <Grid
        label="Coverage"
        columns={coverageColumns}
        rows={rows}
        getRowId={getRowId}
        sort={sort}
        onSortChange={(next) => {
          if (isSortField(next.columnId)) update({ sort: { field: next.columnId, descending: next.descending } });
        }}
        onRowClick={(row) => {
          open(row.id);
        }}
        activeRowId={openId}
        loading={isPending || isPlaceholderData}
        error={isError ? problemText(error, 'The coverage could not be loaded.') : null}
        onRetry={result.refetch}
        resetKey={JSON.stringify([filters.q.trim(), filters.status, filters.hasGaps])}
        infinite={{
          total: result.total,
          hasMore: result.hasMore,
          loadingMore: result.isFetchingNextPage,
          onLoadMore: result.loadMore,
          error: loadMoreError ? problemText(loadMoreError, 'The next instruments could not be loaded.') : null,
          onRetryMore: result.retryMore,
        }}
        emptyState={<CoverageEmpty hasActiveFilters={activeFilterCount(filters) > 0} onClear={clear} />}
      />
    </Card>
  );
}
