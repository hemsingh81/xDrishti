import CheckIcon from '@mui/icons-material/CheckCircleOutlined';
import RemoveIcon from '@mui/icons-material/RemoveCircleOutlineOutlined';
import { Box, Typography } from '@mui/material';
import { useCallback, useMemo } from 'react';
import {
  EvidenceBlock,
  formatCount,
  formatR,
  formatRate,
  HeatGrid,
  MetricValue,
  type HeatGridCell,
} from '@/shared/analytics';
import { numeric } from '@/theme';
import type { CellStats } from '../api/learningQueries';
import { buildGrid, cellsEvidence, expectancyOf, lowerBoundOf, tileLabel, tintAlpha } from '../lib/cells';

interface HeatmapProps {
  cells: readonly CellStats[];
  selectedKey: string | null;
  onOpen: (cellKey: string) => void;
}

/**
 * One stored cell as a tile. Enough evidence: the expectancy (sign printed; tinted by that sign), n and the win-rate lower
 * bound. Not enough: labelled "Not enough" (the grid hatches it), and no expectancy or bound at all, so a thin cell never
 * reads as a winner. The tint and the hatching are an addition to the words, never the signal.
 */
function toGridCell(cell: CellStats): HeatGridCell {
  const expectancy = cell.enough ? cell.expectancyR : null;
  // Gain or loss is read from the printed figure: a value that prints "0.00" is neither.
  const sign = expectancy !== null && /[1-9]/.test(formatR(expectancy)) ? Math.sign(expectancy) : 0;
  const Icon = cell.enough ? CheckIcon : RemoveIcon;
  return {
    key: cell.cellKey,
    name: tileLabel(cell),
    state: expectancyOf(cell),
    shade: sign === 0 ? undefined : { tone: sign > 0 ? 'success' : 'error', alpha: tintAlpha(expectancy ?? 0) },
    content: (
      <>
        <Typography
          component="span"
          variant="caption"
          sx={{ display: 'flex', alignItems: 'center', gap: 0.5, fontWeight: 700 }}
        >
          <Icon aria-hidden sx={{ fontSize: '1.1em' }} />
          {cell.enough ? 'Enough' : 'Not enough'}
        </Typography>
        <Typography component="span" variant="body2" sx={{ display: 'block', mt: 0.25 }}>
          <MetricValue state={expectancyOf(cell)} format={formatR} />
        </Typography>
        {cell.enough && (
          <Typography component="span" variant="caption" sx={{ display: 'block', color: 'text.primary', ...numeric }}>
            n {formatCount(cell.n)}
            <br />
            Win-rate lower bound <MetricValue state={lowerBoundOf(cell)} format={formatRate} />
          </Typography>
        )}
      </>
    ),
  };
}

/** Strategy rows by timeframe columns, one tile per stored cell, drawn by the shared `HeatGrid`. */
export function Heatmap({ cells, selectedKey, onOpen }: HeatmapProps) {
  const grid = useMemo(() => buildGrid(cells), [cells]);
  const byRow = useMemo(
    () =>
      new Map(grid.rows.map((row) => [row.id, new Map([...row.cells].map(([tf, cell]) => [tf, toGridCell(cell)]))])),
    [grid],
  );
  const cellOf = useCallback((rowId: string, timeframe: string) => byRow.get(rowId)?.get(timeframe) ?? null, [byRow]);
  const minTrades = cells[0]?.minTrades;
  return (
    <Box>
      <HeatGrid
        label="Setups by strategy and timeframe"
        regionLabel="Setups heatmap, scrolls sideways"
        rowHeader="Strategy"
        rows={grid.rows.map((row) => ({ id: row.id, label: row.strategy, detail: row.detail }))}
        columns={grid.timeframes.map((timeframe) => ({ id: timeframe, label: timeframe }))}
        cell={cellOf}
        selectedKey={selectedKey}
        onOpen={onOpen}
        legend={`Each tile is one stored cell. A tinted tile has enough evidence and prints its expectancy with a sign. A hatched tile is labelled Not enough and prints no expectancy: it has fewer trades than the minimum${minTrades === undefined ? '' : ` of ${String(minTrades)}`}. Select a tile to open the cell.`}
      />
      <Box sx={{ mt: 1 }}>
        <EvidenceBlock label="Setups evidence" {...cellsEvidence(cells)} />
      </Box>
    </Box>
  );
}
