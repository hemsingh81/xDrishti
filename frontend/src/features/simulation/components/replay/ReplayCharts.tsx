import { Alert, Box, Card, CardContent, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { formatInr, formatPercent } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';
import { TimeSeriesChart, type ChartSeries } from '@/shared/ui/chart';
import { replayEquityQuery } from '../../api/replayApi';

/** Equity against the benchmark and the cash line, and a drawdown strip below it. */
export function ReplayCharts({
  runId,
  active,
  other,
}: {
  runId: string;
  active: boolean;
  /** The other arm of a baseline pair: its equity is drawn as one more line. */
  other?: { id: string; name: string };
}) {
  const equity = useQuery(replayEquityQuery(runId, active));
  const otherEquity = useQuery({ ...replayEquityQuery(other?.id ?? '', active), enabled: other !== undefined });
  if (equity.isPending) return <LoadingState label="Loading the equity curve…" />;
  if (equity.isError) return <ErrorState error={equity.error} />;
  const { points, benchmarkSymbol, benchmarkReason } = equity.data;
  const series: ChartSeries[] = [
    { id: 'equity', name: 'Equity', color: 'accent', data: points.map((p) => [p.session, p.equity] as const) },
    {
      id: 'benchmark',
      name: benchmarkSymbol ?? 'Benchmark',
      color: 'neutral',
      dash: 'dashed',
      data: points.map((p) => [p.session, p.benchmark] as const),
    },
    { id: 'cash', name: 'Cash', color: 'info', dash: 'dotted', data: points.map((p) => [p.session, p.cash] as const) },
  ];
  if (other && otherEquity.data) {
    series.push({
      id: 'other-arm',
      name: other.name,
      color: 'warn',
      dash: 'dashed',
      data: otherEquity.data.points.map((p) => [p.session, p.equity] as const),
    });
  }
  const drawdown: ChartSeries[] = [
    { id: 'drawdown', name: 'Drawdown', color: 'bad', data: points.map((p) => [p.session, p.drawdownPct] as const) },
  ];
  return (
    <Card component="section" aria-labelledby="replay-equity-title">
      <CardContent sx={{ display: 'grid', gap: 1.5, minWidth: 0 }}>
        <Typography id="replay-equity-title" variant="h2" component="h2">
          Equity
        </Typography>
        {benchmarkReason && <Alert severity="info">{`No benchmark line: ${benchmarkReason}`}</Alert>}
        {other && otherEquity.isError && <ErrorState error={otherEquity.error} />}
        {!equity.data.cashComplete && (
          <Typography variant="body2" color="text.secondary">
            The cash line is not complete until the run ends.
          </Typography>
        )}
        <Box sx={{ minWidth: 0 }}>
          <TimeSeriesChart
            label="Replay equity, benchmark and cash"
            series={series}
            valueFormatter={formatInr}
            height={300}
            empty="No sessions stored yet."
          />
        </Box>
        <Box sx={{ minWidth: 0 }}>
          <TimeSeriesChart
            label="Replay drawdown"
            series={drawdown}
            valueFormatter={(v) => `${formatPercent(v)}%`}
            height={120}
            empty="No sessions stored yet."
          />
        </Box>
      </CardContent>
    </Card>
  );
}
