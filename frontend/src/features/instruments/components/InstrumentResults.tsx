import { Card, Divider } from '@mui/material';
import type { UseQueryResult } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import type { InstrumentPage, InstrumentSummary } from '@/shared/api';
import { Grid, type GridSort } from '@/shared/ui/grid';
import { isSortField, pageSizes, type InstrumentFilters } from '../lib/filters';
import { problemText } from '../lib/errors';
import { columnsFor } from './instrumentColumns';

interface InstrumentResultsProps {
  result: UseQueryResult<InstrumentPage>;
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

/** The results card: fills the space under the filters; the list scrolls inside it between the toolbar and the footer. */
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
  const { data, isPending, isError, isPlaceholderData, error } = result;
  const failed = isError && !data;
  const sort: GridSort = { columnId: filters.sort.field, descending: filters.sort.descending };

  return (
    <Card sx={{ display: 'flex', flexDirection: 'column', flex: '1 1 auto', minHeight: { md: 200 } }}>
      {toolbar}
      <Divider />
      <Grid
        label={filters.tab === 'tracked' ? 'Tracked instruments' : 'Instruments'}
        columns={columnsFor(filters.tab)}
        rows={data?.items ?? []}
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
        error={failed ? problemText(error, 'The instruments could not be loaded.') : null}
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
                  onChange({ page });
                },
                onPageSizeChange: (pageSize) => {
                  onChange({ pageSize });
                },
              }
        }
        emptyState={emptyState}
      />
    </Card>
  );
}
