import { Box, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material';
import {
  EvidenceBlock,
  formatCount,
  formatR,
  formatRate,
  formatRatio,
  MetricCell,
  type MetricState,
} from '@/shared/analytics';
import { formatDate } from '@/shared/lib/format';
import { cellWindows, windowLabels, type CellStats } from '../api/learningQueries';
import { basisOf, statState } from '../lib/cells';

const finiteCount = (value: number): MetricState => ({ kind: 'value', value });

interface Line {
  id: string;
  label: string;
  state: (cell: CellStats) => MetricState;
  format: (value: number) => string;
  tone?: 'gain-loss';
}

// Counts are facts and always printed; every derived statistic is insufficient below the server's minimum.
const lines: readonly Line[] = [
  { id: 'n', label: 'Trades (n)', state: (c) => finiteCount(c.n), format: formatCount },
  { id: 'wins', label: 'Wins', state: (c) => finiteCount(c.wins), format: formatCount },
  { id: 'effn', label: 'Effective n (recency-weighted)', state: (c) => finiteCount(c.effectiveN), format: formatRatio },
  {
    id: 'raw',
    label: 'Win rate, raw',
    state: (c) => statState(c, c.winRateRaw, 'No trades in this window'),
    format: formatRate,
  },
  {
    id: 'post',
    label: 'Win rate, shrunk',
    state: (c) => statState(c, c.winRatePosterior, 'Not stored'),
    format: formatRate,
  },
  {
    id: 'low',
    label: 'Win-rate lower bound',
    state: (c) => statState(c, c.winRateLower, 'Not stored'),
    format: formatRate,
  },
  {
    id: 'up',
    label: 'Win-rate upper bound',
    state: (c) => statState(c, c.winRateUpper, 'Not stored'),
    format: formatRate,
  },
  {
    id: 'exp',
    label: 'Expectancy',
    state: (c) => statState(c, c.expectancyR, 'Not stored'),
    format: formatR,
    tone: 'gain-loss',
  },
  { id: 'win', label: 'Average win', state: (c) => statState(c, c.avgWinR, 'No winning trade'), format: formatR },
  { id: 'loss', label: 'Average loss', state: (c) => statState(c, c.avgLossR, 'No losing trade'), format: formatR },
  {
    id: 'pf',
    label: 'Profit factor',
    state: (c) => statState(c, c.profitFactor, 'No losing trade'),
    format: formatRatio,
  },
  {
    id: 'dd',
    label: 'Max drawdown',
    state: (c) => statState(c, c.maxDrawdownR, 'Not stored'),
    format: (v) => `${formatRatio(v)} R`,
  },
  { id: 'mae50', label: 'MAE, median', state: (c) => statState(c, c.maeP50, 'Not stored'), format: formatR },
  { id: 'mae90', label: 'MAE, 90th percentile', state: (c) => statState(c, c.maeP90, 'Not stored'), format: formatR },
  { id: 'mfe50', label: 'MFE, median', state: (c) => statState(c, c.mfeP50, 'Not stored'), format: formatR },
  { id: 'mfe90', label: 'MFE, 90th percentile', state: (c) => statState(c, c.mfeP90, 'Not stored'), format: formatR },
];

/** One cell in its windows: metrics down, windows across, every figure in its honest state. */
export function CellWindowsTable({ cells }: { cells: readonly CellStats[] }) {
  const windows = cellWindows.flatMap((code) => {
    const cell = cells.find((candidate) => candidate.window === code);
    return cell ? [{ code, cell }] : [];
  });
  const first = windows[0]?.cell;
  if (!first) return null;
  const all = windows.find((entry) => entry.code === 'all')?.cell;
  return (
    <Box>
      <TableContainer role="region" aria-label="Cell statistics by window, scrolls sideways" tabIndex={0}>
        <Table size="small" aria-label="Cell statistics by window">
          <TableHead>
            <TableRow>
              <TableCell>Statistic</TableCell>
              {windows.map(({ code }) => (
                <TableCell key={code} align="right">
                  {windowLabels[code]}
                </TableCell>
              ))}
            </TableRow>
          </TableHead>
          <TableBody>
            <TableRow>
              <TableCell component="th" scope="row">
                Evidence
              </TableCell>
              {windows.map(({ code, cell }) => (
                <TableCell key={code} align="right">
                  {cell.enough ? 'Enough' : 'Not enough'}
                </TableCell>
              ))}
            </TableRow>
            {lines.map((line) => (
              <TableRow key={line.id}>
                <TableCell component="th" scope="row">
                  {line.label}
                </TableCell>
                {windows.map(({ code, cell }) => (
                  <MetricCell key={code} state={line.state(cell)} format={line.format} tone={line.tone} />
                ))}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
      <Typography variant="caption" color="text.secondary" component="p" sx={{ mt: 1 }}>
        Minimum {formatCount(first.minTrades)} trades for a statistic. Credible level {formatRate(first.credibleLevel)}.
        Recency half-life {formatCount(first.recencyHalfLifeDays)} days. Prior strength{' '}
        {formatRatio(first.priorStrength)}. The interval is on the win rate; no interval on expectancy is stored.
      </Typography>
      <Box sx={{ mt: 1 }}>
        <EvidenceBlock
          label="Cell evidence"
          n={all?.n ?? null}
          range={null}
          basis={basisOf(windows.map((entry) => entry.cell))}
          population={[`snapshot ${first.snapshotKey}, as of ${formatDate(first.asOf)}`]}
          limits={[first.limits]}
        />
      </Box>
    </Box>
  );
}
