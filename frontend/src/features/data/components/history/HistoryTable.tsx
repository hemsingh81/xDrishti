import {
  Box,
  Chip,
  LinearProgress,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import { formatIstDate, formatNumber } from '@/shared/lib/format';
import type { HistoryInstrument } from '../../api/historyQueries';
import { probeLabel, statusLabel } from '../../lib/history';

const day = (value: string | null) => (value ? formatIstDate(value) : 'none');

function StatusCell({ row }: { row: HistoryInstrument }) {
  const { chunks, backfillStatus } = row;
  const warn = backfillStatus === 'seam_jump' || backfillStatus === 'seam_unresolved';
  return (
    <Box sx={{ display: 'grid', gap: 0.5, minWidth: 120 }}>
      <Chip
        size="small"
        variant="outlined"
        color={warn ? 'warning' : backfillStatus === 'done' ? 'success' : 'default'}
        label={statusLabel(backfillStatus)}
        sx={{ justifySelf: 'start' }}
      />
      {backfillStatus === 'seam_jump' && row.seamJumpPct !== null && (
        <Typography variant="caption">Jump of {row.seamJumpPct.toFixed(1)}% at the seam</Typography>
      )}
      {backfillStatus === 'partial' && chunks.total > 0 && (
        <>
          <LinearProgress
            variant="determinate"
            value={(chunks.done / chunks.total) * 100}
            aria-label={`${row.symbol} chunks done`}
          />
          <Typography variant="caption">
            {formatNumber(chunks.done)}/{formatNumber(chunks.total)} done, {formatNumber(chunks.failed)} failed
          </Typography>
        </>
      )}
    </Box>
  );
}

function probeText(row: HistoryInstrument): string {
  const { probe } = row;
  if (probe.status === 'none') return probeLabel(probe.status);
  const median = probe.medianBps === null ? '' : `, median ${probe.medianBps.toFixed(1)} bps`;
  const within = probe.withinMaxPct === null ? '' : `, ${probe.withinMaxPct.toFixed(0)}% within the limit`;
  return `${probeLabel(probe.status)}${probe.at ? ` ${formatIstDate(probe.at)}` : ''}${median}${within}`;
}

/** One row per tracked instrument; it scrolls sideways inside its box on a phone, never the page. */
export function HistoryTable({ rows }: { rows: HistoryInstrument[] }) {
  return (
    <TableContainer sx={{ maxWidth: '100%', overflowX: 'auto' }}>
      <Table size="small" aria-label="History per instrument">
        <TableHead>
          <TableRow>
            <TableCell>Symbol</TableCell>
            <TableCell>History from</TableCell>
            <TableCell>First CSV bar</TableCell>
            <TableCell>Status</TableCell>
            <TableCell align="right">Chunks</TableCell>
            <TableCell>Probe</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {rows.map((row) => (
            <TableRow key={row.instrumentId}>
              <TableCell component="th" scope="row">
                {row.symbol}
              </TableCell>
              <TableCell>{day(row.historyFrom)}</TableCell>
              <TableCell>{day(row.firstCsvBar)}</TableCell>
              <TableCell>
                <StatusCell row={row} />
              </TableCell>
              <TableCell align="right" sx={{ fontVariantNumeric: 'tabular-nums' }}>
                {formatNumber(row.chunks.done)}/{formatNumber(row.chunks.total)}
              </TableCell>
              <TableCell>{probeText(row)}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </TableContainer>
  );
}
