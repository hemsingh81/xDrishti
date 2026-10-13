import { http, HttpResponse } from 'msw';
import type {
  Drawdown,
  HistoryBenchmark,
  HistoryCoverage,
  HistoryPoint,
  PortfolioBenchmark,
  PortfolioHistory,
  PortfolioTrackResult,
} from '@/shared/api';

/** Made-up id of the benchmark index (Nifty 50 of the made-up master list). */
export const benchmarkInstrumentId = '00000000-0000-4000-8000-00000000be01';

export const buildHistoryPoint = (date: string, overrides: Partial<HistoryPoint> = {}): HistoryPoint => ({
  date,
  value: 100_000,
  invested: 100_000,
  benchmarkValue: 100_000,
  nav: 100,
  benchmarkNav: 100,
  ...overrides,
});

/** Six trading days: the portfolio grows to ₹2,03,400 on ₹1,85,000 invested; the index version grows to ₹1,96,000. */
export const sampleHistoryPoints = (): HistoryPoint[] => [
  buildHistoryPoint('2025-01-01'),
  buildHistoryPoint('2025-06-02', {
    value: 112_000,
    invested: 100_000,
    benchmarkValue: 108_000,
    nav: 112,
    benchmarkNav: 108,
  }),
  buildHistoryPoint('2025-08-01', {
    value: 125_000,
    invested: 125_000,
    benchmarkValue: 128_000,
    nav: 105,
    benchmarkNav: 106,
  }),
  buildHistoryPoint('2026-01-01', {
    value: 150_000,
    invested: 150_000,
    benchmarkValue: 151_000,
    nav: 118,
    benchmarkNav: 117,
  }),
  buildHistoryPoint('2026-06-01', {
    value: 190_000,
    invested: 185_000,
    benchmarkValue: 186_000,
    nav: 130,
    benchmarkNav: 124,
  }),
  buildHistoryPoint('2026-10-09', {
    value: 203_400,
    invested: 185_000,
    benchmarkValue: 196_000,
    nav: 138,
    benchmarkNav: 129,
  }),
];

export const buildCoverage = (overrides: Partial<HistoryCoverage> = {}): HistoryCoverage => ({
  holdings: 4,
  covered: 4,
  unpriced: 0,
  lateStart: 0,
  ...overrides,
});

export const buildHistoryBenchmark = (overrides: Partial<HistoryBenchmark> = {}): HistoryBenchmark => ({
  state: 'ok',
  instrumentId: benchmarkInstrumentId,
  symbol: 'NIFTY 50',
  name: 'Nifty 50',
  tracked: true,
  hasBars: true,
  reason: null,
  asOf: '2026-10-09',
  ...overrides,
});

/** The benchmark could not be used: not tracked, so no bars. */
export const buildUnavailableBenchmark = (overrides: Partial<HistoryBenchmark> = {}): HistoryBenchmark =>
  buildHistoryBenchmark({
    state: 'unavailable',
    tracked: false,
    hasBars: false,
    reason: 'Nifty 50 is not tracked, so it has no stored prices.',
    asOf: null,
    ...overrides,
  });

export const buildDrawdown = (overrides: Partial<Drawdown> = {}): Drawdown => ({
  pct: 6.25,
  from: '2025-06-02',
  to: '2025-08-01',
  ...overrides,
});

export function buildHistory(overrides: Partial<PortfolioHistory> = {}): PortfolioHistory {
  return {
    points: sampleHistoryPoints(),
    coverage: buildCoverage(),
    partial: false,
    benchmark: buildHistoryBenchmark(),
    maxDrawdown: buildDrawdown(),
    benchmarkMaxDrawdown: buildDrawdown({ pct: 1.85 }),
    firstDate: '2025-01-01',
    lastPriceAt: null,
    resolution: 'day',
    rangeFrom: '2025-01-01',
    rangeTo: '2026-10-09',
    ...overrides,
  };
}

/** A history with no point: nothing is tracked yet. */
export const buildEmptyHistory = (overrides: Partial<PortfolioHistory> = {}): PortfolioHistory =>
  buildHistory({
    points: [],
    coverage: buildCoverage({ covered: 0, unpriced: 4 }),
    partial: true,
    maxDrawdown: null,
    benchmarkMaxDrawdown: null,
    firstDate: null,
    rangeFrom: null,
    rangeTo: null,
    ...overrides,
  });

export const buildBenchmarkView = (overrides: Partial<PortfolioBenchmark> = {}): PortfolioBenchmark => ({
  instrumentId: benchmarkInstrumentId,
  symbol: 'NIFTY 50',
  name: 'Nifty 50',
  isDefault: true,
  tracked: true,
  hasBars: true,
  ...overrides,
});

export const buildTrackResult = (overrides: Partial<PortfolioTrackResult> = {}): PortfolioTrackResult => ({
  requested: 3,
  added: 3,
  alreadyTracked: 0,
  notInMaster: 0,
  retired: 0,
  totalTracked: 10,
  hint: 'Run Update data to build their price history.',
  ...overrides,
});

/** Default API mocks of the value history: a complete history, tracked benchmark, track actions that add something. */
export const historyHandlers = [
  http.get('/api/portfolio/history', () => HttpResponse.json(buildHistory())),
  http.get('/api/portfolio/benchmark', () => HttpResponse.json(buildBenchmarkView())),
  http.put('/api/portfolio/benchmark', () => HttpResponse.json(buildBenchmarkView({ isDefault: false }))),
  http.post('/api/portfolio/history/track-holdings', () => HttpResponse.json(buildTrackResult())),
  http.post('/api/portfolio/history/track-benchmark', () =>
    HttpResponse.json(buildTrackResult({ requested: 1, added: 1, totalTracked: 11 })),
  ),
];
