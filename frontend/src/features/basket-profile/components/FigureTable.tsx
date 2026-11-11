import { Box, Table, TableBody, TableCell, TableContainer, TableHead, TableRow } from '@mui/material';
import { Fig } from './Fig';
import type { Figure } from '../lib/figure';

export interface FigureRow {
  id: string;
  label: string;
  view: Figure;
  format: (value: number) => string;
  tone?: 'neutral' | 'gain-loss';
}

interface FigureTableProps {
  /** Names the table and its scrolling region. */
  label: string;
  rows: readonly FigureRow[];
}

/** Label and figure per row; the first column is the row header and stays put while the table scrolls inside its card. */
export function FigureTable({ label, rows }: FigureTableProps) {
  return (
    <Box sx={{ minWidth: 0 }}>
      <TableContainer role="region" aria-label={`${label}, scrolls sideways`} tabIndex={0}>
        <Table size="small" aria-label={label}>
          <TableHead>
            <TableRow>
              <TableCell>Figure</TableCell>
              <TableCell align="right">Value</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.map((row) => (
              <TableRow key={row.id}>
                <TableCell component="th" scope="row" sx={{ position: 'sticky', left: 0, bgcolor: 'background.paper' }}>
                  {row.label}
                </TableCell>
                <TableCell align="right">
                  <Fig view={row.view} format={row.format} tone={row.tone} />
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
    </Box>
  );
}
