import { Box, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material';
import { useMemo } from 'react';
import { EvidenceBlock } from '@/shared/analytics';
import type { CellStats } from '../api/learningQueries';
import { buildGrid, cellsEvidence } from '../lib/cells';
import { HeatTile } from './HeatTile';

interface HeatmapProps {
  cells: readonly CellStats[];
  selectedKey: string | null;
  onOpen: (cellKey: string) => void;
}

/**
 * Strategy rows by timeframe columns, one button per stored cell. It is a real table, so a screen reader gets the same numbers
 * in the same grid (row and column headers, the name of each cell in words); it scrolls sideways inside its own focusable
 * region and keeps the strategy column in view.
 */
export function Heatmap({ cells, selectedKey, onOpen }: HeatmapProps) {
  const grid = useMemo(() => buildGrid(cells), [cells]);
  const minTrades = cells[0]?.minTrades;
  return (
    <Box>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
        Each tile is one stored cell. A tinted tile has enough evidence and prints its expectancy with a sign. A hatched
        tile is labelled Not enough and prints no expectancy: it has fewer trades than the minimum
        {minTrades === undefined ? '' : ` of ${String(minTrades)}`}. Select a tile to open the cell.
      </Typography>
      <TableContainer role="region" aria-label="Setups heatmap, scrolls sideways" tabIndex={0}>
        <Table
          size="small"
          aria-label="Setups by strategy and timeframe"
          sx={{ '& tbody tr:nth-of-type(even)': { backgroundColor: 'transparent' } }}
        >
          <TableHead>
            <TableRow>
              <TableCell sx={{ position: 'sticky', left: 0, zIndex: 1 }}>Strategy</TableCell>
              {grid.timeframes.map((timeframe) => (
                <TableCell key={timeframe}>{timeframe}</TableCell>
              ))}
            </TableRow>
          </TableHead>
          <TableBody>
            {grid.rows.map((row) => (
              <TableRow key={row.id}>
                <TableCell
                  component="th"
                  scope="row"
                  sx={{ position: 'sticky', left: 0, zIndex: 1, backgroundColor: 'background.paper', minWidth: 120 }}
                >
                  <Typography component="span" variant="body2" sx={{ display: 'block', fontWeight: 600 }}>
                    {row.strategy}
                  </Typography>
                  <Typography component="span" variant="caption" color="text.secondary">
                    {row.detail}
                  </Typography>
                </TableCell>
                {grid.timeframes.map((timeframe) => {
                  const cell = row.cells.get(timeframe);
                  return (
                    <TableCell key={timeframe} sx={{ verticalAlign: 'top', p: 0.5 }}>
                      {cell ? (
                        <HeatTile cell={cell} selected={cell.cellKey === selectedKey} onOpen={onOpen} />
                      ) : (
                        <Typography variant="caption" color="text.secondary" sx={{ px: 1 }}>
                          No cell
                        </Typography>
                      )}
                    </TableCell>
                  );
                })}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
      <Box sx={{ mt: 1 }}>
        <EvidenceBlock label="Setups evidence" {...cellsEvidence(cells)} />
      </Box>
    </Box>
  );
}
