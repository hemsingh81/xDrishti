import { Button, Card, Stack, Typography } from '@mui/material';
import type { UseQueryResult } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import type { HoldingPage, HoldingRow } from '@/shared/api';
import { Grid, type GridSort } from '@/shared/ui/grid';
import { problemText } from '../lib/errors';
import { isSortField, pageSizes, type PortfolioFilters } from '../lib/filters';
import { holdingColumns, holdingColumnsWithAccount } from './holdingColumns';

interface HoldingsGridProps {
  result: UseQueryResult<HoldingPage>;
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

/** The holdings card: fills the space under the tiles; the rows scroll inside it between the header and the footer. */
export function HoldingsGrid({
  result,
  filters,
  openId,
  showAccount,
  emptyState,
  onChange,
  onOpen,
}: HoldingsGridProps) {
  const { data, isPending, isError, isPlaceholderData, error } = result;
  const failed = isError && !data;
  const sort: GridSort = { columnId: filters.sort.field, descending: filters.sort.descending };

  return (
    <Card sx={{ display: 'flex', flexDirection: 'column', flex: '1 1 auto', minHeight: 320 }}>
      <Grid
        label="Holdings"
        columns={showAccount ? holdingColumnsWithAccount : holdingColumns}
        rows={data?.items ?? []}
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
        error={failed ? problemText(error, 'The holdings could not be loaded.') : null}
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

/** "No holdings match these filters" with a way to clear them. */
export function NoMatches({ canClear, onClear }: { canClear: boolean; onClear: () => void }) {
  return (
    <Stack spacing={1} sx={{ alignItems: 'center', py: 5 }}>
      <Typography sx={{ fontWeight: 600 }}>No holdings match these filters</Typography>
      {canClear && <Button onClick={onClear}>Clear filters</Button>}
    </Stack>
  );
}
