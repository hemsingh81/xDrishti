import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useNavigate } from 'react-router';
import {
  EvidenceBlock,
  formatCount,
  formatR,
  HeatGrid,
  MetricValue,
  type HeatGridCell,
  type MetricState,
} from '@/shared/analytics';
import { pivotQuery, lenses } from '../api/reportQueries';
import { NOT_RECORDED } from '../hooks/useBasketNames';
import { evidenceOf, NON_ADDITIVE_NOTE } from '../lib/evidence';
import { cellHref } from '../lib/cellLink';
import { metricFromApi } from '../lib/metricFromApi';
import type { ReportPanelProps } from '../lib/reportFilter';
import { QueryPanel } from './Panel';

const words: Record<string, string> = {
  provisional: 'provisional',
  insufficient: 'insufficient data',
  unavailable: 'not available',
  not_evaluable: 'not evaluable',
  empty: 'no records yet',
};
const textOf = (state: MetricState): string =>
  state.kind === 'value' ? formatR(state.value) : (words[state.kind] ?? state.kind);

/** Expectancy by strategy (rows) and timeframe (columns). The server masks thin cells; this only draws what it sent. */
export function StrategyTimeframePanel({ query, min }: ReportPanelProps) {
  const result = useQuery(pivotQuery(query, lenses.pivot));
  const navigate = useNavigate();
  return (
    <QueryPanel title="Strategy by timeframe" query={result}>
      {(pivot) => {
        const rows = pivot.rows.map((row) => ({ id: row, label: row }));
        const columns = pivot.columns.map((column) => ({ id: column, label: column }));
        const cells = new Map(
          pivot.cells.map((cell) => [`${cell.row ?? NOT_RECORDED}\u0000${cell.column ?? NOT_RECORDED}`, cell]),
        );
        const peak = Math.max(
          0,
          ...pivot.cells.map((cell) => (cell.value.state === 'value' ? Math.abs(cell.value.number ?? 0) : 0)),
        );
        const cellOf = (rowId: string, columnId: string): HeatGridCell | null => {
          const cell = cells.get(`${rowId}\u0000${columnId}`);
          if (!cell) return null;
          const state = metricFromApi(cell.value, cell.n, min);
          return {
            key: `${rowId}\u0000${columnId}`,
            name: `${rowId}, ${columnId}: ${textOf(state)}, n ${formatCount(cell.n)}`,
            state,
            shade:
              state.kind === 'value' && peak > 0
                ? { tone: state.value >= 0 ? 'success' : 'error', alpha: 0.08 + 0.3 * (Math.abs(state.value) / peak) }
                : undefined,
            content: (
              <>
                <MetricValue state={state} format={formatR} />
                <Typography component="span" variant="caption" color="text.secondary" sx={{ display: 'block' }}>
                  n {formatCount(cell.n)}
                </Typography>
              </>
            ),
          };
        };
        return (
          <Box sx={{ display: 'grid', gap: 1.5 }}>
            <HeatGrid
              label="Expectancy by strategy and timeframe"
              regionLabel="Strategy by timeframe grid, scrolls sideways"
              rowHeader="Strategy"
              rows={rows}
              columns={columns}
              cell={cellOf}
              // A cell is a strategy and a timeframe: its detail page takes those as filters (P8-11).
              onOpen={(key) => {
                const cell = cells.get(key);
                const href = cell
                  ? cellHref(
                      query,
                      [
                        { dimension: 'strategy', value: cell.row },
                        { dimension: 'timeframe', value: cell.column },
                      ],
                      `${cell.row ?? ''}, ${cell.column ?? ''}`,
                    )
                  : null;
                if (href) void navigate(href);
              }}
              emptyText="No trades"
              legend="Expectancy in R per trade. Hatched cells have too few trades for a figure."
            />
            {pivot.nonAdditive && (
              <Typography variant="body2" color="text.secondary">
                {NON_ADDITIVE_NOTE}
              </Typography>
            )}
            <EvidenceBlock {...evidenceOf(pivot.evidence)} compact />
          </Box>
        );
      }}
    </QueryPanel>
  );
}
