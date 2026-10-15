import { Box, Typography } from '@mui/material';
import { useId } from 'react';
import { formatNumber } from '@/shared/lib/format';
import { PagedTable, type PagedColumn } from './PagedTable';

interface DifferenceListProps<T> {
  title: string;
  empty: string;
  rows: readonly T[];
  total: number;
  columns: readonly PagedColumn<T>[];
  rowKey: (row: T) => string;
}

/** One list of a demat check: its title with the total, the rows 50 at a time, or the sentence that says there are none. */
export function DifferenceList<T>({ title, empty, rows, total, columns, rowKey }: DifferenceListProps<T>) {
  const headingId = useId();
  return (
    <Box component="section" aria-labelledby={headingId}>
      <Typography id={headingId} component="h5" variant="h4" sx={{ mb: 0.5 }}>
        {`${title} (${formatNumber(total)})`}
      </Typography>
      {total === 0 ? (
        <Typography variant="body2" color="text.secondary">
          {empty}
        </Typography>
      ) : (
        <PagedTable label={title} noun="securities" rows={rows} total={total} columns={columns} rowKey={rowKey} />
      )}
    </Box>
  );
}
