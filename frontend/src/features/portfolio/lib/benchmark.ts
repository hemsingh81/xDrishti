import type { HistoryBenchmark, HoldingsSummary } from '@/shared/api';
import { formatDayMonth, formatPercent } from '@/shared/lib/format';
import { directionOf, xirrOf, xirrReasons, type Direction } from './figures';
import { priceReturnBasis } from './income';

/** The index name for labels: the master-list display name, else the symbol, else Nifty 50 (the default benchmark). */
export const benchmarkName = (benchmark: Pick<HistoryBenchmark, 'name' | 'symbol'> | undefined): string =>
  benchmark?.name ?? benchmark?.symbol ?? 'Nifty 50';

/** The legend entry of the benchmark line: "Nifty 50 (same cash flows)". */
export const benchmarkSeriesName = (benchmark: Pick<HistoryBenchmark, 'name' | 'symbol'> | undefined): string =>
  `${benchmarkName(benchmark)} (same cash flows)`;

export type FigureOrReason = { value: number } | { reason: string; short: string; label: '—' | 'n/m' };

const unavailableReason = (name: string) =>
  `${name} has no price history from the first purchase, so the same cash flows cannot be put into it. Track it and run Update data.`;

/**
 * The benchmark XIRR of the totals, or why there is none. `reason` is what the history says about the benchmark
 * (tracking, missing bars); the summary's own state decides which case applies.
 */
export function benchmarkXirrOf(
  summary: Pick<HoldingsSummary, 'benchmarkXirr' | 'benchmarkState'>,
  { name = 'Nifty 50', reason = null }: { name?: string; reason?: string | null } = {},
): FigureOrReason {
  const state = summary.benchmarkState;
  if (state === 'ok' && summary.benchmarkXirr !== null) return { value: summary.benchmarkXirr };
  if (state === 'insufficient')
    return { reason: xirrReasons.insufficient, short: 'Needs 30+ days of history', label: '—' };
  if (state === 'n_m') {
    return {
      reason: `Not meaningful: the annualised rate of the same cash flows in ${name} is beyond ±1,000 %.`,
      short: 'Not meaningful (beyond ±1,000 %)',
      label: 'n/m',
    };
  }
  return { reason: reason ?? unavailableReason(name), short: `No ${name} history`, label: '—' };
}

/** Alpha in percentage points (the XIRR minus the benchmark XIRR), or why there is none. */
export function alphaOf(
  summary: Pick<HoldingsSummary, 'alpha' | 'benchmarkXirr' | 'benchmarkState'> & {
    returns: HoldingsSummary['returns'];
  },
  options: { name?: string; reason?: string | null } = {},
): FigureOrReason {
  if (summary.alpha !== null) return { value: summary.alpha };
  const benchmark = benchmarkXirrOf(summary, options);
  if (!('value' in benchmark)) return { ...benchmark, reason: `Alpha needs the benchmark XIRR. ${benchmark.reason}` };
  const own = xirrOf(summary.returns);
  if (!('value' in own)) return { reason: `Alpha needs your XIRR. ${own.reason}`, short: own.short, label: own.label };
  return { reason: 'Alpha is not available.', short: 'Not available', label: '—' };
}

/** "+2.40 pp", "−1.10 pp", "0.00 pp": a difference of two rates in percentage points. */
export function signedPoints(value: number): string {
  const direction = directionOf(value, 2);
  const text = `${formatPercent(Math.abs(value))} pp`;
  if (direction === 'up') return `+${text}`;
  return direction === 'down' ? `−${text}` : `0.00 pp`;
}

export const pointsDirection = (value: number): Direction => directionOf(value, 2);

type SummaryBenchmark = Pick<
  HoldingsSummary,
  'alpha' | 'benchmarkXirr' | 'benchmarkState' | 'benchmarkAsOf' | 'alphaBasis'
>;
type BenchmarkContext = Pick<HistoryBenchmark, 'name' | 'symbol' | 'reason'> | undefined;

/** Under the benchmark XIRR: which index, or in few words why there is no figure. */
export function benchmarkXirrHint(summary: SummaryBenchmark, benchmark: BenchmarkContext): string {
  const figure = benchmarkXirrOf(summary, { name: benchmarkName(benchmark), reason: benchmark?.reason });
  return 'value' in figure ? `${benchmarkName(benchmark)}, same cash flows` : figure.short;
}

/**
 * Under alpha: what it is (the XIRR minus the benchmark's, both price returns before dividends and charges, ADR 0026). The index
 * has no live quote: when the portfolio is valued at the prices of a later trading day than the index close
 * (`live_vs_last_close`), the hint says against which close alpha was worked out.
 */
export function alphaHint(
  summary: SummaryBenchmark & { returns: HoldingsSummary['returns'] },
  benchmark: BenchmarkContext,
): string {
  const figure = alphaOf(summary, { name: benchmarkName(benchmark), reason: benchmark?.reason });
  if (!('value' in figure)) return figure.short;
  if (summary.alphaBasis === 'live_vs_last_close' && summary.benchmarkAsOf) {
    return `Live prices vs ${benchmarkName(benchmark)} close of ${formatDayMonth(summary.benchmarkAsOf)} · ${priceReturnBasis}`;
  }
  return `XIRR minus benchmark XIRR · ${priceReturnBasis}`;
}
