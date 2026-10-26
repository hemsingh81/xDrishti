import { Box, Typography } from '@mui/material';
import { DataTable, formatSigned, type Column } from '@/shared/analytics';
import type { CompareFanPoint } from '../api/compareQueries';

const W = 600;
const H = 220;
const PAD = 8;

const columns: readonly Column<CompareFanPoint>[] = [
  { kind: 'text', id: 'index', header: 'Trades', align: 'right', render: (p) => p.index },
  { kind: 'text', id: 'p05', header: '5th percentile', align: 'right', render: (p) => formatSigned(p.p05) },
  { kind: 'text', id: 'p50', header: 'Median', align: 'right', render: (p) => formatSigned(p.p50) },
  { kind: 'text', id: 'p95', header: '95th percentile', align: 'right', render: (p) => formatSigned(p.p95) },
  { kind: 'text', id: 'paper', header: 'Paper', align: 'right', render: (p) => formatSigned(p.paper) },
];

/** The paper line over the backtest fan (R14): cumulative R by trade. A plain SVG with the same numbers as a table beside it. */
export function CompareFan({ fan }: { fan: readonly CompareFanPoint[] }) {
  const first = fan[0];
  const last = fan[fan.length - 1];
  if (!first || !last)
    return <Typography>No fan: the band is not a figure yet, so there is nothing to draw.</Typography>;
  const values = fan.flatMap((p) => [p.p05, p.p95, p.paper, 0]);
  const lo = Math.min(...values);
  const span = Math.max(...values) - lo || 1;
  const x = (i: number) => PAD + (fan.length === 1 ? (W - 2 * PAD) / 2 : (i * (W - 2 * PAD)) / (fan.length - 1));
  const y = (v: number) => H - PAD - ((v - lo) / span) * (H - 2 * PAD);
  const line = (pick: (p: CompareFanPoint) => number) => fan.map((p, i) => `${x(i)},${y(pick(p))}`).join(' ');
  const area = `${line((p) => p.p95)} ${[...fan]
    .reverse()
    .map((p, i) => `${x(fan.length - 1 - i)},${y(p.p05)}`)
    .join(' ')}`;
  const summary = `Cumulative R over ${last.index} trades: paper ${formatSigned(last.paper)}R; backtest 5th to 95th percentile ${formatSigned(last.p05)}R to ${formatSigned(last.p95)}R, median ${formatSigned(last.p50)}R`;
  return (
    <Box>
      <Box
        component="svg"
        viewBox={`0 0 ${W} ${H}`}
        role="img"
        aria-label={summary}
        sx={{ width: '100%', maxWidth: 720, height: 'auto', color: 'primary.main' }}
      >
        <line
          x1={PAD}
          x2={W - PAD}
          y1={y(0)}
          y2={y(0)}
          stroke="currentColor"
          strokeOpacity={0.3}
          strokeDasharray="2 4"
        />
        <polygon points={area} fill="currentColor" fillOpacity={0.15} stroke="none" />
        <polyline
          points={line((p) => p.p50)}
          fill="none"
          stroke="currentColor"
          strokeOpacity={0.6}
          strokeWidth={1.5}
          strokeDasharray="6 4"
          vectorEffect="non-scaling-stroke"
        />
        <polyline
          points={line((p) => p.paper)}
          fill="none"
          stroke="currentColor"
          strokeWidth={2.5}
          vectorEffect="non-scaling-stroke"
        />
      </Box>
      <Typography variant="caption" color="text.secondary" component="p" sx={{ mt: 0 }}>
        {summary}. Solid line: paper. Shaded: backtest 5th to 95th percentile. Dashed: backtest median.
      </Typography>
      <Box component="details" sx={{ mt: 1 }}>
        <summary>Show the same numbers as a table</summary>
        <DataTable label="Fan of cumulative R" columns={columns} rows={fan} rowKey={(p) => String(p.index)} />
      </Box>
    </Box>
  );
}
