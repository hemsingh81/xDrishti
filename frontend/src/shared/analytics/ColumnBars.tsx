import { Box, Button, Table, TableBody, TableCell, TableContainer, TableHead, TableRow } from '@mui/material';
import { useId, useState } from 'react';
import { numeric } from '@/theme';
import { formatCount } from './format';

export interface ColumnBin {
  id: string;
  label: string;
  count: number;
}

interface ColumnBarsProps {
  /** Names the histogram and its table. */
  label: string;
  bins: readonly ColumnBin[];
  /** Heading of the bin column of the table ("R"). */
  binHeader?: string;
  /** What the numbers count ("Trades"). */
  countHeader?: string;
}

/**
 * A histogram of counts drawn with CSS (no chart library): one column per bin. The columns are decorative; "Show as table"
 * prints the same numbers as text. A bin with no trades has a count of 0 (a count, not a missing figure); a histogram with
 * no bins is not drawn by the caller (it prints a state instead).
 */
export function ColumnBars({ label, bins, binHeader = 'Bin', countHeader = 'Trades' }: ColumnBarsProps) {
  const [table, setTable] = useState(false);
  const tableId = useId();
  const peak = Math.max(1, ...bins.map((bin) => bin.count));
  return (
    <Box sx={{ minWidth: 0 }}>
      <Box
        role="group"
        aria-label={label}
        sx={{ display: 'flex', alignItems: 'flex-end', gap: '2px', height: 140, overflowX: 'auto' }}
      >
        {bins.map((bin) => (
          <Box key={bin.id} sx={{ flex: '1 0 14px', minWidth: 14, height: '100%', display: 'grid', alignItems: 'end' }}>
            <Box
              aria-hidden
              sx={{
                height: `${((bin.count / peak) * 100).toFixed(1)}%`,
                minHeight: bin.count > 0 ? 2 : 0,
                backgroundColor: 'primary.main',
                borderRadius: '2px 2px 0 0',
              }}
            />
          </Box>
        ))}
      </Box>
      <Button
        size="small"
        aria-expanded={table}
        aria-controls={tableId}
        onClick={() => {
          setTable((open) => !open);
        }}
        sx={{ minHeight: 44, mt: 0.5 }}
      >
        {table ? `Hide ${label} table` : `Show ${label} as table`}
      </Button>
      <TableContainer id={tableId} hidden={!table} role="region" aria-label={`${label}, scrolls sideways`} tabIndex={0}>
        <Table size="small" aria-label={`${label} table`}>
          <TableHead>
            <TableRow>
              <TableCell>{binHeader}</TableCell>
              <TableCell align="right">{countHeader}</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {bins.map((bin) => (
              <TableRow key={bin.id}>
                <TableCell component="th" scope="row">
                  {bin.label}
                </TableCell>
                <TableCell align="right" sx={numeric}>
                  {formatCount(bin.count)}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
    </Box>
  );
}
