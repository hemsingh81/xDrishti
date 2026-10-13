import { Box, Button, Stack, Typography } from '@mui/material';
import type { ReactNode } from 'react';
import type { PortfolioHistory } from '@/shared/api';
import { formatDate, formatIstTime, formatNumber } from '@/shared/lib/format';
import { stateAlpha } from '@/theme';
import { useToast } from '@/shared/ui/toastContext';
import { useTrackBenchmark, useTrackHoldings } from '../api/historyMutations';
import { benchmarkName } from '../lib/benchmark';
import { trackBenchmarkToast, trackErrorText, trackHoldingsToast } from '../lib/trackText';

function Note({ children, action }: { children: ReactNode; action?: ReactNode }) {
  return (
    <Stack
      component="li"
      direction="row"
      sx={{ alignItems: 'center', flexWrap: 'wrap', columnGap: 1.5, rowGap: 0.5, minHeight: 30 }}
    >
      <Typography variant="body2" color="text.secondary">
        {children}
      </Typography>
      {action}
    </Stack>
  );
}

interface CoverageLineProps {
  history: PortfolioHistory;
  /** Open lots still on the import's default date (from the totals). */
  defaultDateLots: number;
  scope: { account: string | null; bucket: string | null };
  /** The history shown is of the previous range or scope while the new one loads: dimmed, and nothing to act on yet. */
  stale?: boolean;
}

/**
 * Under the chart: what the history covers and what could make it better. "Track my holdings" and "Track the benchmark"
 * start no job; the next Update data builds the bars, and the toast says so.
 */
export function CoverageLine({ history, defaultDateLots, scope, stale = false }: CoverageLineProps) {
  const toast = useToast();
  const trackHoldings = useTrackHoldings(scope);
  const trackBenchmark = useTrackBenchmark();
  const { coverage, benchmark } = history;
  const name = benchmarkName(benchmark);

  const onTrackHoldings = () => {
    trackHoldings.mutate(undefined, {
      onSuccess: (result) => {
        const { message, severity } = trackHoldingsToast(result);
        toast.notify(message, severity);
      },
      onError: (error) => {
        toast.notify(trackErrorText(error, 'The holdings could not be tracked.'), 'error');
      },
    });
  };
  const onTrackBenchmark = () => {
    trackBenchmark.mutate(undefined, {
      onSuccess: (result) => {
        const { message, severity } = trackBenchmarkToast(result, name);
        toast.notify(message, severity);
      },
      onError: (error) => {
        toast.notify(trackErrorText(error, `${name} could not be tracked.`), 'error');
      },
    });
  };

  const benchmarkMissing = benchmark.state === 'unavailable';
  const canTrackBenchmark =
    benchmarkMissing && benchmark.instrumentId !== null && (!benchmark.tracked || !benchmark.hasBars);
  const notes: ReactNode[] = [];

  if (coverage.holdings > 0) {
    notes.push(
      <Note
        key="coverage"
        action={
          coverage.unpriced > 0 && (
            <Button
              size="small"
              variant="outlined"
              loading={trackHoldings.isPending}
              disabled={stale}
              onClick={onTrackHoldings}
            >
              Track my holdings
            </Button>
          )
        }
      >
        {`History covers ${formatNumber(coverage.covered)} of ${formatNumber(coverage.holdings)} holdings`}
        {coverage.unpriced > 0 && ` · ${formatNumber(coverage.unpriced)} without price history are left out`}
      </Note>,
    );
  }
  if (coverage.lateStart > 0) {
    const one = coverage.lateStart === 1;
    notes.push(
      <Note key="late">
        {`${formatNumber(coverage.lateStart)} ${one ? 'holding starts' : 'holdings start'} at ${one ? 'its' : 'their'} first stored price`}
      </Note>,
    );
  }
  if (benchmarkMissing) {
    notes.push(
      <Note
        key="benchmark"
        action={
          canTrackBenchmark && (
            <Button
              size="small"
              variant="outlined"
              loading={trackBenchmark.isPending}
              disabled={stale}
              onClick={onTrackBenchmark}
            >
              Track the benchmark
            </Button>
          )
        }
      >
        {`Benchmark needs ${name} history`}
        {benchmark.reason && <Box component="span">{` · ${benchmark.reason}`}</Box>}
      </Note>,
    );
  }
  if (defaultDateLots > 0 && history.firstDate) {
    notes.push(
      <Note key="default-date">
        {`${formatNumber(defaultDateLots)} ${defaultDateLots === 1 ? 'lot uses' : 'lots use'} the default date, so the history starts on ${formatDate(history.firstDate)}`}
      </Note>,
    );
  }
  if (history.lastPriceAt) {
    notes.push(
      <Note key="live">{`Today's point uses the latest price (${formatIstTime(history.lastPriceAt)} IST)`}</Note>,
    );
  }
  // The index has no live quote: its line stops at its last close while the portfolio line goes on to a later day.
  const lastDate = history.points.at(-1)?.date;
  if (benchmark.state === 'ok' && benchmark.asOf && lastDate && lastDate > benchmark.asOf) {
    notes.push(<Note key="benchmark-as-of">{`${name} valued at the close of ${formatDate(benchmark.asOf)}`}</Note>);
  }

  if (notes.length === 0) return null;
  return (
    <Stack
      component="ul"
      aria-label="History coverage"
      aria-busy={stale || undefined}
      sx={{ listStyle: 'none', m: 0, mt: 1.5, p: 0, opacity: stale ? stateAlpha.busy : 1 }}
    >
      {notes}
    </Stack>
  );
}
