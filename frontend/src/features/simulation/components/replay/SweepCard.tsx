import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router';
import { formatDate, formatInr, formatNumber, formatPercent } from '@/shared/lib/format';
import { ErrorState, Facts, LoadingState, type Fact } from '@/shared/ui';
import { replaySweepQuery, useCancelSweep, type ReplaySweep } from '../../api/replayApi';
import { HindsightBanner } from './HindsightBanner';

const none = 'n/a';
/** A fraction as a percent ("0.12" is "12.00%"). */
const frac = (v: number | null) => (v === null ? none : `${formatPercent(v * 100)}%`);
/** A figure that is already in percent. */
const pc = (v: number | null) => (v === null ? none : `${formatPercent(v)}%`);

const stateWords: Record<string, string> = {
  queued: 'Waiting',
  running: 'Running',
  cancelling: 'Stopping',
  completed: 'Completed',
  cancelled: 'Cancelled',
  failed: 'Failed',
};
const sweepWords: Record<string, string> = {
  running: 'Running: some starts are still waiting or running.',
  completed: 'Completed: every start finished.',
  cancelled: 'Cancelled: the sweep was stopped; the starts that finished are below.',
  failed: 'Failed: at least one start failed; the starts that finished are below.',
};

function spreadFacts(s: ReplaySweep['spread']): Fact[] {
  const range = (v: { min: number; median: number; max: number } | null, fmt: (n: number) => string) =>
    v ? `min ${fmt(v.min)}, median ${fmt(v.median)}, max ${fmt(v.max)}` : none;
  return [
    { label: 'Starts counted', value: s.starts },
    { label: 'CAGR (yearly growth)', value: range(s.cagr, (n) => frac(n)) },
    { label: 'Max drawdown (worst fall)', value: range(s.maxDrawdownPct, (n) => pc(n)) },
    { label: 'Share of starts with CAGR above 0', value: frac(s.shareCagrPositive) },
    { label: 'Share of starts beating the benchmark', value: frac(s.shareBeatingBenchmark) },
  ];
}

/**
 * One sweep (SR-02): the hindsight labels, "this sweep adds N replay looks", the state, the per-start table and the spread over
 * the completed starts only. Read again while it runs; it can be cancelled while it runs.
 */
export function SweepCard({ sweepId }: { sweepId: string }) {
  const sweep = useQuery(replaySweepQuery(sweepId));
  const cancel = useCancelSweep(sweepId);
  const d = sweep.data;
  return (
    <Card component="section" aria-labelledby="replay-sweep-title">
      <CardContent sx={{ display: 'grid', gap: 1.5, minWidth: 0 }}>
        <Typography id="replay-sweep-title" variant="h2" component="h2">
          Start-date sweep
        </Typography>
        {sweep.isPending && <LoadingState label="Loading the sweep…" />}
        {sweep.isError && <ErrorState error={sweep.error} />}
        {d && (
          <>
            <HindsightBanner run={d} />
            <Typography sx={{ fontWeight: 600 }}>{`This sweep adds ${String(d.looksAdded)} replay looks.`}</Typography>
            <Typography variant="body2">{`Ends ${formatDate(d.to)}. ${sweepWords[d.state] ?? d.state}`}</Typography>
            {!d.complete && d.state !== 'running' && (
              <Alert severity="info">
                {`Partial: ${String(d.completedStarts)} of ${String(d.starts.length)} starts completed. The spread covers the completed starts only.`}
              </Alert>
            )}
            {d.state === 'running' && (
              <Box>
                <Button
                  variant="outlined"
                  color="warning"
                  disabled={cancel.isPending}
                  onClick={() => {
                    cancel.mutate();
                  }}
                  sx={{ minHeight: 44 }}
                >
                  Cancel sweep
                </Button>
                {cancel.isError && (
                  <Alert severity="error" role="alert" sx={{ mt: 1 }}>
                    The sweep could not be cancelled; it may have just finished.
                  </Alert>
                )}
              </Box>
            )}
            <TableContainer sx={{ maxHeight: 420 }}>
              <Table size="small" stickyHeader aria-label="Sweep starts">
                <TableHead>
                  <TableRow>
                    <TableCell>Start</TableCell>
                    <TableCell>State</TableCell>
                    <TableCell align="right">CAGR</TableCell>
                    <TableCell align="right">Max drawdown</TableCell>
                    <TableCell align="right">Final equity</TableCell>
                    <TableCell align="right">Trades</TableCell>
                    <TableCell>Beats benchmark</TableCell>
                    <TableCell>Replay</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {d.starts.map((s) => (
                    <TableRow key={s.runId}>
                      <TableCell>{formatDate(s.from)}</TableCell>
                      <TableCell>
                        <Chip
                          size="small"
                          variant="outlined"
                          color={s.state === 'completed' ? 'success' : s.state === 'failed' ? 'error' : 'default'}
                          label={stateWords[s.state] ?? s.state}
                        />
                        {s.state === 'running' && ` ${String(s.sessionsDone)} of ${String(s.sessionsTotal)} sessions`}
                        {s.state === 'failed' && s.failReason && (
                          <Typography variant="caption" component="p">
                            {s.failReason}
                          </Typography>
                        )}
                      </TableCell>
                      <TableCell align="right">{frac(s.cagr)}</TableCell>
                      <TableCell align="right">{pc(s.maxDrawdownPct)}</TableCell>
                      <TableCell align="right">{s.finalEquity === null ? none : formatInr(s.finalEquity)}</TableCell>
                      <TableCell align="right">{s.trades === null ? none : formatNumber(s.trades)}</TableCell>
                      <TableCell>{s.beatsBenchmark === null ? none : s.beatsBenchmark ? 'Yes' : 'No'}</TableCell>
                      <TableCell>
                        <Button
                          component={Link}
                          to={`/simulation/replay?run=${s.runId}`}
                          size="small"
                          aria-label={`Open the replay from ${formatDate(s.from)}`}
                          sx={{ minHeight: 44 }}
                        >
                          Open
                        </Button>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
            <Typography variant="h3" component="h3">
              Spread over the completed starts
            </Typography>
            <Facts items={spreadFacts(d.spread)} />
          </>
        )}
      </CardContent>
    </Card>
  );
}
