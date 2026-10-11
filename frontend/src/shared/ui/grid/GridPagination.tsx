import { Box, TablePagination } from '@mui/material';
import { formatNumber } from '@/shared/lib/format';
import type { GridPagination as GridPaginationProps } from './types';

/** The fixed footer: "Rows per page", "1–50 of 3,204" and the page buttons. */
export function GridPagination({ pagination }: { pagination: GridPaginationProps }) {
  const { page, pageSize, pageSizeOptions, total, onPageChange, onPageSizeChange } = pagination;
  return (
    <Box sx={{ flexShrink: 0, borderTop: 1, borderColor: 'divider' }}>
      <TablePagination
        component="div"
        sx={{ '& .MuiTablePagination-toolbar': { flexWrap: 'wrap', justifyContent: 'flex-end', rowGap: 0.5 } }}
        count={total}
        page={page - 1}
        rowsPerPage={pageSize}
        rowsPerPageOptions={[...pageSizeOptions]}
        labelRowsPerPage="Rows per page"
        labelDisplayedRows={({ from, to, count }) =>
          count === 0 ? '0 results' : `${formatNumber(from)}–${formatNumber(to)} of ${formatNumber(count)}`
        }
        onPageChange={(_event, next) => {
          onPageChange(next + 1);
        }}
        onRowsPerPageChange={(event) => {
          onPageSizeChange(Number(event.target.value));
        }}
      />
    </Box>
  );
}
