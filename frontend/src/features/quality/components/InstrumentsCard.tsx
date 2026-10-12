import { Card, Divider } from '@mui/material';
import { useInfiniteRows } from '@/shared/lib/useInfiniteRows';
import type { QualityInstrumentRow } from '@/shared/api';
import { Grid, type GridSort } from '@/shared/ui/grid';
import { instrumentsList, instrumentsParamsOf } from '../api/qualityQueries';
import type { QualityFilterState } from '../hooks/useQualityFilters';
import { problemText } from '../lib/errors';
import { activeFilterCount, isSortField } from '../lib/filters';
import { InstrumentsEmpty } from './InstrumentsEmpty';
import { InstrumentsToolbar } from './InstrumentsToolbar';
import { NeverRun } from './NeverRun';
import { qualityColumns } from './qualityColumns';

const getRowId = (row: QualityInstrumentRow) => row.instrumentId;

interface InstrumentsCardProps {
  filterState: QualityFilterState;
  /** No check has run and no instrument has a result: the card says what to press instead of listing the rows. */
  neverRun: boolean;
}

/**
 * The tracked instruments with bars and the result of their last check. The card fills the space under the summary
 * ribbon (from `md`; on a phone it has a height of its own): the rows scroll inside it and further pages load as the end
 * comes into view. A row opens the Inspect drawer.
 */
export function InstrumentsCard({ filterState, neverRun }: InstrumentsCardProps) {
  const { filters, update, clear, openId, open } = filterState;
  const result = useInfiniteRows(instrumentsList(filters));
  const { rows, isPending, isError, isPlaceholderData, error, loadMoreError } = result;
  const sort: GridSort = { columnId: filters.sort.field, descending: filters.sort.descending };

  return (
    <Card
      component="section"
      aria-label="Instruments"
      sx={{
        display: 'flex',
        flexDirection: 'column',
        flex: '1 1 auto',
        height: { xs: 'max(420px, 65dvh)', md: 'auto' },
        minHeight: { md: 240 },
      }}
    >
      <InstrumentsToolbar filters={filters} onChange={update} onClear={clear} />
      <Divider />
      {neverRun ? (
        <NeverRun />
      ) : (
        <Grid
          label="Instruments"
          columns={qualityColumns}
          rows={rows}
          getRowId={getRowId}
          sort={sort}
          onSortChange={(next) => {
            if (isSortField(next.columnId)) update({ sort: { field: next.columnId, descending: next.descending } });
          }}
          onRowClick={(row) => {
            open(row.instrumentId);
          }}
          activeRowId={openId}
          loading={isPending || isPlaceholderData}
          error={isError ? problemText(error, 'The instruments could not be loaded.') : null}
          onRetry={result.refetch}
          resetKey={JSON.stringify(instrumentsParamsOf(filters))}
          infinite={{
            total: result.total,
            hasMore: result.hasMore,
            loadingMore: result.isFetchingNextPage,
            onLoadMore: result.loadMore,
            error: loadMoreError ? problemText(loadMoreError, 'The next instruments could not be loaded.') : null,
            onRetryMore: result.retryMore,
          }}
          emptyState={<InstrumentsEmpty hasActiveFilters={activeFilterCount(filters) > 0} onClear={clear} />}
        />
      )}
    </Card>
  );
}
