import { Box, Card, CardContent, Divider, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useEffect, useId } from 'react';
import { Grid, type GridSort } from '@/shared/ui/grid';
import type { DataCoverageRow } from '@/shared/api';
import { coverageQuery } from '../api/dataQueries';
import type { useCoverageFilters } from '../hooks/useCoverageFilters';
import { problemText } from '../lib/errors';
import { activeFilterCount, isSortField, pageSizes } from '../lib/filters';
import { coverageColumns } from './coverageColumns';
import { CoverageEmpty } from './CoverageEmpty';
import { CoverageToolbar } from './CoverageToolbar';

const getRowId = (row: DataCoverageRow) => row.id;

/**
 * What is stored per instrument: first bar, last bar (highlighted when behind), bars, source and status. The page
 * scrolls; on `md` and up the rows scroll inside the card so the header and the paging stay in view.
 */
export function CoverageCard({ filterState }: { filterState: ReturnType<typeof useCoverageFilters> }) {
  const { filters, update, clear, openId, open } = filterState;
  const titleId = useId();
  const result = useQuery(coverageQuery(filters));
  const { data, isPending, isError, isPlaceholderData, error } = result;
  const failed = isError && !data;
  const sort: GridSort = { columnId: filters.sort.field, descending: filters.sort.descending };

  // An old link can point past the last page; step back to it instead of showing an empty list.
  useEffect(() => {
    if (data?.items.length === 0 && data.total > 0 && filters.page > 1) {
      update({ page: Math.ceil(data.total / data.pageSize) }, { replace: true });
    }
  }, [data, filters.page, update]);

  return (
    <Card component="section" aria-labelledby={titleId} sx={{ mb: 2, display: 'flex', flexDirection: 'column' }}>
      <CardContent>
        <Typography id={titleId} variant="h2" sx={{ mb: 1.5 }}>
          Coverage
        </Typography>
        <CoverageToolbar filters={filters} onChange={update} onClear={clear} />
      </CardContent>
      <Divider />
      <Box sx={{ display: 'flex', flexDirection: 'column', height: { md: 560 } }}>
        <Grid
          label="Coverage"
          columns={coverageColumns}
          rows={data?.items ?? []}
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
          error={failed ? problemText(error, 'The coverage could not be loaded.') : null}
          onRetry={() => {
            void result.refetch();
          }}
          pagination={
            failed
              ? undefined
              : {
                  page: filters.page,
                  pageSize: filters.pageSize,
                  pageSizeOptions: pageSizes,
                  total: data?.total ?? 0,
                  onPageChange: (page) => {
                    update({ page });
                  },
                  onPageSizeChange: (pageSize) => {
                    update({ pageSize });
                  },
                }
          }
          emptyState={<CoverageEmpty hasActiveFilters={activeFilterCount(filters) > 0} onClear={clear} />}
        />
      </Box>
    </Card>
  );
}
