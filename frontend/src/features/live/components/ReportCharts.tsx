import { Stack, Typography } from '@mui/material';
import { CategoryChart, TimeSeriesChart, type ChartSeries } from '@/shared/ui/chart';
import type { LiveVsPaperView } from '../api/reportQueries';

const edge = (v: number | null) => (v === null ? '' : String(v));
const binName = (lo: number | null, hi: number | null) =>
  lo === null ? `< ${edge(hi)}` : hi === null ? `>= ${edge(lo)}` : `${edge(lo)} to ${edge(hi)}`;

/** The cumulative R of the paired set (plan prices, paper, live) and the histogram of live entry slippage in basis points. A non-value is words, never a flat line. */
export function ReportCharts({ view }: { view: LiveVsPaperView }) {
  const lines = [
    {
      id: 'plan',
      name: 'Plan prices',
      color: 'neutral' as const,
      dash: 'dotted' as const,
      line: view.equity.planPrices,
    },
    { id: 'paper', name: 'Paper', color: 'info' as const, dash: 'dashed' as const, line: view.equity.paper },
    { id: 'live', name: 'Live', color: 'accent' as const, dash: 'solid' as const, line: view.equity.live },
  ];
  const drawn: ChartSeries[] = lines
    .filter((l) => l.line.state === 'value' && l.line.points.length > 0)
    .map((l) => ({
      id: l.id,
      name: l.name,
      color: l.color,
      dash: l.dash,
      data: l.line.points.map((p) => [p.date, p.value] as const),
    }));
  const h = view.histogram;
  return (
    <Stack sx={{ gap: 2 }}>
      <section aria-label="Cumulative R of the paired set">
        <Typography variant="h3" component="h3">
          Cumulative R of the paired set
        </Typography>
        {drawn.length > 0 ? (
          <>
            <TimeSeriesChart
              label="Cumulative R of the paired set"
              series={drawn}
              valueFormatter={(v) => `${v.toFixed(3)} R`}
              height={240}
            />
            <Typography variant="body2" color="text.secondary">
              {`Paired tickets only, in session order; n at the end: ${String(view.pairsTotal)}. Not an account curve (no sizing, no compounding).`}
            </Typography>
          </>
        ) : (
          <Typography>No line to draw: no closed pair, or an input is unavailable.</Typography>
        )}
        {lines
          .filter((l) => l.line.state === 'unavailable')
          .map((l) => (
            <Typography key={l.id} variant="body2">{`${l.name}: unavailable. ${l.line.reason ?? ''}`}</Typography>
          ))}
      </section>
      <section aria-label="Live slippage histogram">
        <Typography variant="h3" component="h3">
          Live entry slippage (basis points)
        </Typography>
        {h.state === 'value' ? (
          <CategoryChart
            label="Live entry slippage in basis points"
            data={{
              kind: 'stacked',
              categories: h.bins.map((b) => ({ id: binName(b.lo, b.hi), name: binName(b.lo, b.hi) })),
              stacks: [{ id: 'count', name: 'Entries', color: 0, values: h.bins.map((b) => b.count) }],
            }}
            valueFormatter={(v) => `${String(v)} entries`}
            height={260}
          />
        ) : (
          <Typography>
            {h.state === 'insufficient'
              ? `Insufficient: n = ${String(h.n)}; a histogram needs 10 observations.`
              : 'No live entry slippage yet.'}
          </Typography>
        )}
        <Typography
          variant="body2"
          color="text.secondary"
        >{`Reference kind: ${h.kind}; n = ${String(h.n)}.`}</Typography>
      </section>
    </Stack>
  );
}
