import { Box, Button, Stack, Typography } from '@mui/material';
import type { HoldingGroup, HoldingRow } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { Grid, type GridSort } from '@/shared/ui/grid';
import { groupPageSize } from '../api/groupQueries';
import { useGroupHoldings } from '../hooks/useGroupHoldings';
import { problemText } from '../lib/errors';
import type { GroupBy, PortfolioFilters } from '../lib/filters';
import { holdingColumns, holdingColumnsWithAccount } from './holdingColumns';

/** What the opened groups need to know about the page: the scope, and the open holding. */
export interface GroupScope {
  filters: PortfolioFilters;
  by: GroupBy;
  /** Re-read the opened groups this often while the market is open (false: never). */
  pollMs: number | false;
  /** The holdings of more than one account are listed: show the account column. */
  showAccount: boolean;
  openId: string | null;
  onOpen: (id: string) => void;
}

const getRowId = (holding: HoldingRow) => holding.id;
const getRowLabel = (holding: HoldingRow) => holding.symbol;

interface GroupChildrenProps {
  group: HoldingGroup;
  scope: GroupScope;
  /** Id of the region (the expander's `aria-controls`). */
  id: string;
}

/**
 * The holdings of an opened group in the existing holding columns, 50 at a time: a "Show N more" row at the end loads the
 * next page. No scroller of its own and no scroll observer: the group simply grows, and the page scrolls.
 */
export function GroupChildren({ group, scope, id }: GroupChildrenProps) {
  const { filters, by } = scope;
  const result = useGroupHoldings(filters, by, group.key, scope.pollMs);
  const sort: GridSort = { columnId: filters.sort.field, descending: filters.sort.descending };
  const left = result.total - result.rows.length;
  const pageSize = result.lastPage?.pageSize ?? groupPageSize;

  return (
    <Box
      id={id}
      sx={{
        bgcolor: 'action.hover',
        borderLeft: 3,
        borderColor: 'primary.main',
        borderBottom: 1,
        borderBottomColor: 'divider',
      }}
    >
      <Grid
        label={`${group.name} holdings`}
        columns={scope.showAccount ? holdingColumnsWithAccount : holdingColumns}
        rows={result.rows}
        getRowId={getRowId}
        getRowLabel={getRowLabel}
        sort={sort}
        onRowClick={(holding) => {
          scope.onOpen(holding.id);
        }}
        activeRowId={scope.openId}
        loading={result.isPending || result.isPlaceholderData}
        skeletonRowCount={Math.max(1, Math.min(group.holdings, 3))}
        error={result.isError ? problemText(result.error, 'The holdings of this group could not be loaded.') : null}
        onRetry={result.refetch}
        emptyState={
          <Typography color="text.secondary" sx={{ p: 2 }}>
            No holdings in this group
          </Typography>
        }
      />
      {result.rows.length > 0 && (
        <Stack direction="row" sx={{ px: 1.5, py: 0.75, alignItems: 'center', gap: 1.5, flexWrap: 'wrap' }}>
          <Typography variant="caption" color="text.secondary" role="status">
            {`Showing ${formatNumber(result.rows.length)} of ${formatNumber(result.total)}`}
          </Typography>
          {result.loadMoreError ? (
            <>
              <Typography variant="caption" color="error">
                Could not load more holdings
              </Typography>
              <Button size="small" onClick={result.retryMore}>
                Retry
              </Button>
            </>
          ) : (
            left > 0 && (
              // aria-disabled, not disabled: a disabled button drops the keyboard focus (and does so at every poll refetch).
              <Button
                size="small"
                aria-disabled={result.isFetching}
                className={result.isFetching ? 'Mui-disabled' : undefined}
                onClick={() => {
                  if (!result.isFetching) result.loadMore();
                }}
              >
                {result.isFetchingNextPage ? 'Loading…' : `Show ${formatNumber(Math.min(pageSize, left))} more`}
              </Button>
            )
          )}
        </Stack>
      )}
    </Box>
  );
}
