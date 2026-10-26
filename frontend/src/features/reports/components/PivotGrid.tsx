import { Box, Typography } from '@mui/material';
import { formatCount, HeatGrid, MetricValue, type HeatGridCell } from '@/shared/analytics';
import type { PivotView } from '../api/reportQueries';
import { NOT_RECORDED, type useBasketNames } from '../hooks/useBasketNames';
import { isShaded, metricFormat, peakOf, shadeOf, stateText } from '../lib/heatCell';
import { metricFromApi } from '../lib/metricFromApi';
import { dimensionLabel, TALL_GRID_ROWS, type PivotMetric } from '../lib/pivotLimits';
import type { CellInfo } from './CellPanel';

interface PivotGridProps {
  pivot: PivotView;
  min: number | null;
  names: ReturnType<typeof useBasketNames>;
  label: string;
  onOpen: (info: CellInfo) => void;
  selectedKey: string | null;
}

const at = (row: string | null, column: string | null) => `${row ?? NOT_RECORDED}\u0000${column ?? NOT_RECORDED}`;

/**
 * Any two-dimension pivot as a heat grid. A cell below the minimum is the server's `insufficient` (hatched, "n of at least N",
 * no figure); a cell with no trade says "No trades"; the colour scale reads value cells only.
 */
export function PivotGrid({ pivot, min, names, label, onOpen, selectedKey }: PivotGridProps) {
  const metric = pivot.metric as PivotMetric;
  const format = metricFormat(metric);
  const rowName = (value: string) => names.part(pivot.rowDimension, value);
  const columnName = (value: string) => names.part(pivot.columnDimension, value);
  const cells = new Map(pivot.cells.filter((cell) => cell.n > 0).map((cell) => [at(cell.row, cell.column), cell]));
  const peak = isShaded(metric) ? peakOf(pivot.cells) : 0;
  const cellOf = (rowId: string, columnId: string): HeatGridCell | null => {
    const cell = cells.get(at(rowId, columnId));
    if (!cell) return null;
    const state = metricFromApi(cell.value, cell.n, min);
    const title = `${rowName(rowId)}, ${columnName(columnId)}`;
    return {
      key: at(rowId, columnId),
      name: `${title}: ${stateText(state, format)}, n ${formatCount(cell.n)}`,
      state,
      shade: isShaded(metric) ? shadeOf(state, peak) : undefined,
      content: (
        <>
          <MetricValue state={state} format={format} />
          <Typography component="span" variant="caption" color="text.secondary" sx={{ display: 'block' }}>
            n {formatCount(cell.n)}
          </Typography>
        </>
      ),
    };
  };
  const tall = pivot.rows.length > TALL_GRID_ROWS;
  return (
    <Box sx={tall ? { maxHeight: 560, overflow: 'auto' } : undefined}>
      <HeatGrid
        label={label}
        regionLabel={`${label}, scrolls sideways`}
        rowHeader={dimensionLabel(pivot.rowDimension)}
        rows={pivot.rows.map((row) => ({ id: row, label: rowName(row) }))}
        columns={pivot.columns.map((column) => ({ id: column, label: columnName(column) }))}
        cell={cellOf}
        selectedKey={selectedKey}
        onOpen={(key) => {
          const cell = cells.get(key);
          if (!cell) return;
          onOpen({
            key,
            title: `${rowName(cell.row ?? NOT_RECORDED)}, ${columnName(cell.column ?? NOT_RECORDED)}`,
            n: cell.n,
            state: metricFromApi(cell.value, cell.n, min),
            format,
            evidence: pivot.evidence,
            parts: [
              { dimension: pivot.rowDimension, value: cell.row },
              { dimension: pivot.columnDimension, value: cell.column },
            ],
          });
        }}
        emptyText="No trades"
        legend="Hatched = insufficient data (too few trades for a figure). Descriptive, not a ranking."
      />
    </Box>
  );
}
