import FolderOffIcon from '@mui/icons-material/FolderOffOutlined';
import { Alert, Box, Typography } from '@mui/material';
import { useMemo } from 'react';
import type { CsvFilePage, CsvFileRow } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import type { InfiniteRows } from '@/shared/lib/useInfiniteRows';
import { Grid } from '@/shared/ui/grid';
import { csvErrorText } from '../lib/errors';
import { csvFileColumns } from './csvFileColumns';
import { CsvListContext } from './csvListContext';

interface CsvFilesPanelProps {
  result: InfiniteRows<CsvFileRow, CsvFilePage>;
  /** Search or a state filter is on (an empty list then means "no match", not "no files"). */
  filtered: boolean;
  /** Identifies the list (search and state): a new one scrolls back to the top. */
  listKey: string;
  selected: ReadonlySet<string>;
  full: boolean;
  busy: boolean;
  onToggle: (row: CsvFileRow) => void;
  onUndo: (row: CsvFileRow) => void;
}

const getRowId = (file: CsvFileRow) => file.name;

/**
 * The list part of the review dialog: the folder's files in a grid that scrolls inside the dialog and loads the next
 * 100 as its end comes into view; a missing folder and an empty folder say so. The choice is kept by name, so it
 * survives loading more rows and a change of search or filter.
 */
export function CsvFilesPanel({
  result,
  filtered,
  listKey,
  selected,
  full,
  busy,
  onToggle,
  onUndo,
}: CsvFilesPanelProps) {
  const { lastPage, loadMoreError } = result;
  const list = useMemo(() => ({ selected, full, busy, onToggle, onUndo }), [selected, full, busy, onToggle, onUndo]);

  if (lastPage && !lastPage.folderExists) {
    return (
      <Alert severity="warning" icon={<FolderOffIcon />}>
        The CSV folder {lastPage.folder} was not found. Check that data/Stocks_CSV exists and is mounted into the API.
      </Alert>
    );
  }
  const unmapped = lastPage?.counts.unmapped ?? 0;
  return (
    <>
      {unmapped > 0 && (
        <Typography variant="body2" color="text.secondary">
          {formatNumber(unmapped)} {unmapped === 1 ? 'file cannot' : 'files cannot'} be loaded (unknown or ambiguous
          symbol, or an invalid name) and cannot be selected.
        </Typography>
      )}
      <Box
        sx={{
          display: 'flex',
          flexDirection: 'column',
          height: { xs: '45dvh', md: 380 },
          border: 1,
          borderColor: 'divider',
          borderRadius: 1,
          overflow: 'hidden',
        }}
      >
        <CsvListContext value={list}>
          <Grid
            label="CSV files"
            columns={csvFileColumns}
            rows={result.rows}
            getRowId={getRowId}
            loading={result.isPending || result.isPlaceholderData}
            error={result.isError ? csvErrorText(result.error, 'The CSV files could not be loaded.') : null}
            onRetry={result.refetch}
            resetKey={listKey}
            infinite={{
              total: result.total,
              hasMore: result.hasMore,
              loadingMore: result.isFetchingNextPage,
              onLoadMore: result.loadMore,
              error: loadMoreError ? csvErrorText(loadMoreError, 'The next files could not be loaded.') : null,
              onRetryMore: result.retryMore,
            }}
            emptyState={
              <Typography color="text.secondary" sx={{ py: 3, textAlign: 'center' }}>
                {filtered ? 'No files match.' : 'No CSV files in the folder.'}
              </Typography>
            }
          />
        </CsvListContext>
      </Box>
    </>
  );
}
