import { Typography } from '@mui/material';
import type { CorporateAction, CorporateActionPage } from '@/shared/api';
import type { InfiniteRows } from '@/shared/lib/useInfiniteRows';
import { Grid } from '@/shared/ui/grid';
import type { ActionsView } from '../lib/actions';
import { problemText } from '../lib/errors';
import { actionColumns } from './actionColumns';
import { ActionRowActions } from './ActionRowActions';

const getRowId = (action: CorporateAction) => action.id;
const columns = actionColumns((action) => <ActionRowActions action={action} />);

const emptyTexts: Readonly<Record<ActionsView, string>> = {
  all: 'No corporate actions yet. “Detect now” looks for splits and bonus issues in your stored day bars, or add one by hand.',
  suggested: 'No suggestions waiting for a decision.',
  applied: 'No action is applied. Prices are shown as they were loaded.',
  ignored: 'Nothing is ignored.',
};

interface ActionsListProps {
  result: InfiniteRows<CorporateAction, CorporateActionPage>;
  view: ActionsView;
}

/** The corporate actions in a grid that fills the tab and loads further rows as it is scrolled. */
export function ActionsList({ result, view }: ActionsListProps) {
  const { loadMoreError } = result;
  return (
    <Grid
      label="Corporate actions list"
      columns={columns}
      rows={result.rows}
      getRowId={getRowId}
      loading={result.isPending || result.isPlaceholderData}
      error={result.isError ? problemText(result.error, 'The corporate actions could not be loaded.') : null}
      onRetry={result.refetch}
      resetKey={view}
      infinite={{
        total: result.total,
        hasMore: result.hasMore,
        loadingMore: result.isFetchingNextPage,
        onLoadMore: result.loadMore,
        error: loadMoreError ? problemText(loadMoreError, 'The next actions could not be loaded.') : null,
        onRetryMore: result.retryMore,
      }}
      emptyState={
        <Typography color="text.secondary" sx={{ p: 3, textAlign: 'center' }}>
          {emptyTexts[view]}
        </Typography>
      }
    />
  );
}
