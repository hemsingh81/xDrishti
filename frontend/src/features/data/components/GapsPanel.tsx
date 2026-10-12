import { Box, Card, Divider, Typography } from '@mui/material';
import type { GapRow, GapsOverview } from '@/shared/api';
import { useInfiniteRows } from '@/shared/lib/useInfiniteRows';
import { Grid } from '@/shared/ui/grid';
import { gapsList } from '../api/dataQueries';
import type { GapsViewState } from '../hooks/useGapsView';
import { problemText } from '../lib/errors';
import { gapsViewLabels, gapsViews, isGapsView, type GapsView } from '../lib/gaps';
import { FilterChips } from './FilterChips';
import { gapColumns } from './gapColumns';
import { InfoPopover } from './InfoPopover';

const getRowId = (gap: GapRow) => `${gap.instrumentId}:${gap.date}`;

const countOf = (counts: GapsOverview, view: GapsView) =>
  view === 'all' ? counts.open + counts.unfillable + counts.filled : counts[view];

/**
 * The sessions that have missing 1-minute bars (ADR 0017 §3): instrument, day, bars found against expected, attempts,
 * state and the last attempt. Filter by state with the chips (counts from the overview); the list loads further rows as
 * it is scrolled.
 */
export function GapsPanel({ state, counts }: { state: GapsViewState; counts: GapsOverview }) {
  const { view, update } = state;
  const result = useInfiniteRows(gapsList(view));
  const { loadMoreError } = result;

  return (
    <Card sx={{ display: 'flex', flexDirection: 'column', flex: '1 1 auto', minHeight: 0 }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, px: 1.5, py: 1 }}>
        <FilterChips
          label="Gap status"
          value={view}
          options={gapsViews.map((value) => ({ value, label: gapsViewLabels[value], count: countOf(counts, value) }))}
          onChange={(next) => {
            if (isGapsView(next)) update(next, { replace: true });
          }}
        />
        <InfoPopover label="About gaps">
          <Typography variant="body2">
            Sessions of tracked instruments with missing 1-minute bars. &quot;Re-fetch gaps&quot; in the update menu
            tries open and unfillable ones again.
          </Typography>
        </InfoPopover>
      </Box>
      <Divider />
      <Grid
        label="Gaps"
        columns={gapColumns}
        rows={result.rows}
        getRowId={getRowId}
        loading={result.isPending || result.isPlaceholderData}
        error={result.isError ? problemText(result.error, 'The gaps could not be loaded.') : null}
        onRetry={result.refetch}
        resetKey={view}
        infinite={{
          total: result.total,
          hasMore: result.hasMore,
          loadingMore: result.isFetchingNextPage,
          onLoadMore: result.loadMore,
          error: loadMoreError ? problemText(loadMoreError, 'The next gaps could not be loaded.') : null,
          onRetryMore: result.retryMore,
        }}
        emptyState={
          <Typography color="text.secondary" sx={{ p: 3, textAlign: 'center' }}>
            {view === 'all' ? 'No gaps have been found.' : `No ${gapsViewLabels[view].toLowerCase()} gaps.`}
          </Typography>
        }
      />
    </Card>
  );
}
