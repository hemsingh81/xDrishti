import { Card, Divider } from '@mui/material';
import type { ReactNode } from 'react';
import type { InstrumentSummary } from '@/shared/api';
import type { InfiniteRows } from '@/shared/lib/useInfiniteRows';
import { Grid, type GridSort } from '@/shared/ui/grid';
import { searchParamsOf } from '../api/instrumentsQueries';
import { isSortField, type InstrumentFilters } from '../lib/filters';
import { problemText } from '../lib/errors';
import { columnsFor } from './instrumentColumns';

interface InstrumentResultsProps {
  result: InfiniteRows<InstrumentSummary>;
  filters: InstrumentFilters;
  openId: string | null;
  selectedIds: ReadonlySet<string>;
  onSelectionChange: (ids: ReadonlySet<string>) => void;
  /** The bulk bar, above the list. */
  toolbar: ReactNode;
  /** Shown when the list is empty. */
  emptyState: ReactNode;
  onChange: (patch: Partial<InstrumentFilters>) => void;
  onOpen: (id: string) => void;
}

const getRowId = (item: InstrumentSummary) => item.id;
const getRowLabel = (item: InstrumentSummary) => item.symbol;

/** The results card: fills the space under the filters; the list scrolls inside it between the toolbar and the footer and
 * loads further pages as the end comes into view. */
export function InstrumentResults({
  result,
  filters,
  openId,
  selectedIds,
  onSelectionChange,
  toolbar,
  emptyState,
  onChange,
  onOpen,
}: InstrumentResultsProps) {
  const { rows, isPending, isError, isPlaceholderData, error, loadMoreError } = result;
  const sort: GridSort = { columnId: filters.sort.field, descending: filters.sort.descending };

  return (
    <Card sx={{ display: 'flex', flexDirection: 'column', flex: '1 1 auto', minHeight: { md: 200 } }}>
      {toolbar}
      <Divider />
      <Grid
        label={filters.tab === 'tracked' ? 'Tracked instruments' : 'Instruments'}
        columns={columnsFor(filters.tab)}
        rows={rows}
        getRowId={getRowId}
        getRowLabel={getRowLabel}
        selectedIds={selectedIds}
        onSelectionChange={onSelectionChange}
        sort={sort}
        onSortChange={(next) => {
          if (isSortField(next.columnId)) onChange({ sort: { field: next.columnId, descending: next.descending } });
        }}
        onRowClick={(item) => {
          onOpen(item.id);
        }}
        activeRowId={openId}
        loading={isPending || isPlaceholderData}
        error={isError ? problemText(error, 'The instruments could not be loaded.') : null}
        onRetry={result.refetch}
        resetKey={JSON.stringify(searchParamsOf(filters))}
        infinite={{
          total: result.total,
          hasMore: result.hasMore,
          loadingMore: result.isFetchingNextPage,
          onLoadMore: result.loadMore,
          error: loadMoreError ? problemText(loadMoreError, 'The next instruments could not be loaded.') : null,
          onRetryMore: result.retryMore,
        }}
        emptyState={emptyState}
      />
    </Card>
  );
}
