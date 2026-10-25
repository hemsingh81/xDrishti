import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import {
  ColumnBars,
  EvidenceBlock,
  formatCount,
  formatR,
  formatRate,
  HeatGrid,
  MetricValue,
  NonValue,
  type ColumnBin,
  type HeatGridCell,
} from '@/shared/analytics';
import { maeMfeQuery, type MaeMfeView } from '../api/reportQueries';
import { evidenceOf } from '../lib/evidence';
import { asNonValue, metricFromApi } from '../lib/metricFromApi';
import type { ReportPanelProps } from '../lib/reportFilter';
import { QueryPanel } from './Panel';

type Group = MaeMfeView['groups'][number];
type Grid = NonNullable<Group['grid']>;
const noop = () => undefined;
const r = (value: number) => String(value);

/** Band names from edges: one between each pair, then "or more"; `openFirst` adds a "below" band. */
const bands = (edges: readonly number[], openFirst: boolean): string[] => {
  const labels = openFirst ? [`below ${r(edges[0] ?? 0)}`] : [];
  for (let i = 0; i + 1 < edges.length; i++) labels.push(`${r(edges[i] ?? 0)} to ${r(edges[i + 1] ?? 0)}`);
  labels.push(`${r(edges[edges.length - 1] ?? 0)} or more`);
  return labels.map((label) => `${label} R`);
};

function GridView({ grid }: { grid: Grid }) {
  const rows = bands(grid.maeEdges, false).map((label, i) => ({ id: String(i), label }));
  const columns = bands(grid.resultEdges, true).map((label, i) => ({ id: String(i), label }));
  const peak = Math.max(1, ...grid.counts.flat());
  const winnersFrom = grid.resultEdges.findIndex((edge) => edge >= 0) + 1;
  const cell = (rowId: string, columnId: string): HeatGridCell | null => {
    const count = grid.counts[Number(rowId)]?.[Number(columnId)] ?? 0;
    const row = rows[Number(rowId)]?.label ?? rowId;
    const column = columns[Number(columnId)]?.label ?? columnId;
    return {
      key: `${rowId}:${columnId}`,
      name: `Adverse excursion ${row}, result ${column}: ${formatCount(count)} trades`,
      state: { kind: 'value', value: count },
      shade:
        count > 0
          ? { tone: Number(columnId) >= winnersFrom ? 'success' : 'error', alpha: 0.08 + 0.3 * (count / peak) }
          : undefined,
      content: <span>{formatCount(count)}</span>,
    };
  };
  return (
    <HeatGrid
      label="Adverse excursion by result"
      regionLabel="MAE by result grid, scrolls sideways"
      rowHeader="MAE"
      rows={rows}
      columns={columns}
      cell={cell}
      onOpen={noop}
      emptyText="0"
      legend="Trades by their worst move against the entry (rows) and their result (columns). Counts, shaded by size; green columns are winners."
    />
  );
}

function GroupBlock({ group, min }: { group: Group; min: number | null }) {
  const p90 = metricFromApi(group.winnersMaeP90, group.excursionN, min);
  if (!group.grid || !group.mfe) {
    return <NonValue state={asNonValue(p90.kind === 'value' ? { kind: 'empty' } : p90)} />;
  }
  const edges = group.mfe.edges;
  const bins: ColumnBin[] = [
    ...group.mfe.counts.map((count, i) => ({
      id: String(i),
      label: `${r(edges[i] ?? 0)} to ${r(edges[i + 1] ?? 0)} R`,
      count,
    })),
    { id: 'above', label: `${r(edges[edges.length - 1] ?? 0)} R or more`, count: group.mfe.aboveCount },
  ];
  return (
    <Box sx={{ display: 'grid', gap: 1.5 }}>
      <Typography variant="body2">
        {`${formatCount(group.excursionN)} of ${formatCount(group.n)} trades have a recorded excursion.`}
      </Typography>
      <Box component="ul" aria-label="Targets reached" sx={{ m: 0, pl: 2.5 }}>
        {group.reachedMfe.map((level) => (
          <li key={level.atR}>
            <Typography variant="body2">
              {`${formatCount(level.count)} of ${formatCount(group.excursionN)} trades reached +${r(level.atR)} R at some point (${formatRate(level.share)}).`}
            </Typography>
          </li>
        ))}
      </Box>
      <Typography variant="body2" component="div">
        {"Winners' adverse excursion, 90th percentile: "}
        <MetricValue state={p90} format={formatR} />
      </Typography>
      <GridView grid={group.grid} />
      <Typography variant="h3" component="h3" sx={{ fontSize: 'body1.fontSize' }}>
        Favourable excursion
      </Typography>
      <ColumnBars label="Favourable excursion" bins={bins} binHeader="MFE" />
    </Box>
  );
}

/** R8: MAE and MFE as counts in bins (no scatter: nothing is sampled), for the whole selection. */
export function MaeMfeReport({ query, min }: ReportPanelProps) {
  const result = useQuery(maeMfeQuery(query));
  return (
    <QueryPanel title="MAE and MFE" query={result}>
      {({ groups, evidence }) => (
        <Box sx={{ display: 'grid', gap: 1.5 }}>
          {groups.length === 0 ? (
            <NonValue state={{ kind: 'empty', reason: 'No trades in this selection.' }} />
          ) : (
            groups.map((group) => <GroupBlock key={group.key} group={group} min={min} />)
          )}
          <Typography variant="body2" color="text.secondary">
            Counts only, from the stored best and worst move of each trade; nothing is sampled. Descriptive, in-sample.
          </Typography>
          <EvidenceBlock {...evidenceOf(evidence)} compact />
        </Box>
      )}
    </QueryPanel>
  );
}
