import type {
  QualityBucketRow,
  QualityBucketValues,
  QualityCheckResults,
  QualityCounts,
  QualityGate,
  QualityInstrumentDetail,
  QualityInstrumentRow,
  QualityOverview,
  QualityPhase,
  QualityRun,
} from '@/shared/api';

/** Builders for the Data quality screen (P1-14a): rows, details, runs, the overview and the gate. */

export const qualityId = (n: number) => `00000000-0000-4000-8000-0000000007${String(n).padStart(2, '0')}`;
export const qualityRunId = (n: number) => `00000000-0000-4000-8000-0000000006${String(n).padStart(2, '0')}`;

export const checkedAt = '2026-10-12T15:52:00+05:30';

export const aggregateTimeframes = ['5m', '10m', '15m', '30m', '1h', '1D', '1W', '1M'] as const;

const allAggregates = (status: string, overrides: Record<string, string> = {}): Record<string, string> =>
  Object.fromEntries(aggregateTimeframes.map((tf) => [tf, overrides[tf] ?? status]));

export const buildQualityRow = (overrides: Partial<QualityInstrumentRow> = {}): QualityInstrumentRow => ({
  instrumentId: qualityId(1),
  symbol: 'NSE:RELIANCE',
  name: 'RELIANCE INDUSTRIES LTD',
  type: 'EQUITY',
  status: 'pass',
  score: 100,
  coverage: 100,
  barsChecked: 1_875,
  missingMinutes: 0,
  checks: {
    completeness: 'pass',
    sanity: 'pass',
    spikes: 'pass',
    aggregates: 'pass',
    freshness: 'pass',
    officialDaily: 'notAvailable',
  },
  aggregates: allAggregates('pass'),
  issues: [],
  source: 'csv',
  checkedAt,
  runId: qualityRunId(1),
  ...overrides,
});

/** Passes; a warning for missing minutes; fails on two aggregates; fails on freshness; never checked. */
export const sampleQualityRows: readonly QualityInstrumentRow[] = [
  buildQualityRow(),
  buildQualityRow({
    instrumentId: qualityId(2),
    symbol: 'NSE:TCS',
    name: 'TATA CONSULTANCY SERVICES LTD',
    status: 'warning',
    score: 92,
    coverage: 98.4,
    missingMinutes: 30,
    source: 'mixed',
    checks: {
      completeness: 'warning',
      sanity: 'pass',
      spikes: 'pass',
      aggregates: 'pass',
      freshness: 'pass',
      officialDaily: 'notAvailable',
    },
    issues: ['30 one-minute bars are missing (1.6 % of the expected).'],
  }),
  buildQualityRow({
    instrumentId: qualityId(3),
    symbol: 'NSE:INFY',
    name: 'INFOSYS LTD',
    status: 'fail',
    score: 75,
    aggregates: allAggregates('pass', { '15m': 'fail', '1h': 'fail' }),
    checks: {
      completeness: 'pass',
      sanity: 'pass',
      spikes: 'pass',
      aggregates: 'fail',
      freshness: 'pass',
      officialDaily: 'notAvailable',
    },
    issues: ['2 aggregate buckets differ from the 1-minute bars (15m, 1h).'],
    source: 'dhan',
  }),
  buildQualityRow({
    instrumentId: qualityId(4),
    symbol: 'NSE:HDFCBANK',
    name: 'HDFC BANK LTD',
    status: 'fail',
    score: 85,
    barsChecked: 1_500,
    missingMinutes: 0,
    checks: {
      completeness: 'pass',
      sanity: 'pass',
      spikes: 'pass',
      aggregates: 'pass',
      freshness: 'fail',
      officialDaily: 'notAvailable',
    },
    issues: ['The last bar is 2 trading days old.'],
    source: 'dhan',
  }),
  buildQualityRow({
    instrumentId: qualityId(5),
    symbol: 'NSE:NEWCO',
    name: 'NEW COMPANY LTD',
    status: 'notChecked',
    score: null,
    coverage: null,
    barsChecked: null,
    missingMinutes: null,
    checks: null,
    aggregates: null,
    issues: [],
    source: 'dhan',
    checkedAt: null,
    runId: null,
  }),
];

const values = (overrides: Partial<QualityBucketValues> = {}): QualityBucketValues => ({
  open: 1_530,
  high: 1_532.4,
  low: 1_529.1,
  close: 1_531,
  volume: 81_250,
  bars: 15,
  ...overrides,
});

export const buildBucketRow = (overrides: Partial<QualityBucketRow> = {}): QualityBucketRow => ({
  timeframe: '15m',
  ts: '2026-10-12T09:15:00+05:30',
  stored: values(),
  recomputed: values(),
  differs: false,
  fields: [],
  ...overrides,
});

/** Matching buckets of several timeframes plus, when `tampered`, a 15m bucket whose stored close and volume are wrong. */
export function sampleBuckets(tampered: boolean): QualityBucketRow[] {
  return [
    buildBucketRow({
      timeframe: '5m',
      ts: '2026-10-12T09:15:00+05:30',
      stored: values({ bars: 5 }),
      recomputed: values({ bars: 5 }),
    }),
    buildBucketRow({
      timeframe: '5m',
      ts: '2026-10-12T09:20:00+05:30',
      stored: values({ bars: 5 }),
      recomputed: values({ bars: 5 }),
    }),
    buildBucketRow({ timeframe: '15m', ts: '2026-10-12T09:15:00+05:30' }),
    tampered
      ? buildBucketRow({
          timeframe: '15m',
          ts: '2026-10-12T09:30:00+05:30',
          stored: values({ close: 1_529.5, volume: 80_000 }),
          recomputed: values({ close: 1_531.25, volume: 81_250 }),
          differs: true,
          fields: ['close', 'volume'],
        })
      : buildBucketRow({ timeframe: '15m', ts: '2026-10-12T09:30:00+05:30' }),
    buildBucketRow({
      timeframe: '1h',
      ts: '2026-10-12T09:15:00+05:30',
      ...(tampered
        ? {
            stored: null,
            recomputed: values({ bars: 60 }),
            differs: true,
            fields: ['missing'],
          }
        : { stored: values({ bars: 60 }), recomputed: values({ bars: 60 }) }),
    }),
    buildBucketRow({
      timeframe: '1D',
      ts: '2026-10-12T00:00:00+05:30',
      stored: values({ bars: 375 }),
      recomputed: values({ bars: 375 }),
    }),
  ];
}

/** The checks behind a row: a pass everywhere unless the row's statuses say otherwise. */
export function buildChecks(row: QualityInstrumentRow): QualityCheckResults {
  const status = (check: keyof NonNullable<QualityInstrumentRow['checks']>) => row.checks?.[check] ?? 'pass';
  const missing = row.missingMinutes ?? 0;
  const aggregateStatus = status('aggregates');
  const failing = aggregateStatus === 'fail';
  return {
    completeness: {
      status: status('completeness'),
      expectedMinutes: 1_875,
      foundMinutes: 1_875 - missing,
      missingMinutes: missing,
      missingPercent: (missing / 1_875) * 100,
      unfillableMinutes: 0,
      sessions: ['2026-10-09', '2026-10-12'].map((date, index) => ({
        date,
        expected: 375,
        found: 375 - (index === 0 ? missing : 0),
        missing: index === 0 ? missing : 0,
        unfillable: false,
      })),
    },
    sanity: {
      status: status('sanity'),
      barsChecked: row.barsChecked ?? 0,
      badBars: status('sanity') === 'fail' ? 1 : 0,
      examples:
        status('sanity') === 'fail' ? [{ ts: '2026-10-12T10:00:00+05:30', problem: 'High is below the close.' }] : [],
    },
    spikes: {
      status: status('spikes'),
      spikes: status('spikes') === 'warning' ? 1 : 0,
      largeMoves: 0,
      medianMovePercent: 0.04,
      thresholdPercent: 1,
      examples:
        status('spikes') === 'warning' ? [{ ts: '2026-10-12T11:00:00+05:30', movePercent: 2.5, reversed: true }] : [],
    },
    aggregates: {
      status: aggregateStatus,
      compared: 100,
      differences: failing ? 2 : 0,
      byTimeframe: Object.fromEntries(
        aggregateTimeframes.map((tf) => [
          tf,
          {
            status: row.aggregates?.[tf] ?? 'pass',
            compared: 10,
            differences: row.aggregates?.[tf] === 'fail' ? 1 : 0,
          },
        ]),
      ),
    },
    freshness: {
      status: status('freshness'),
      lastBar: status('freshness') === 'fail' ? '2026-10-08T15:29:00+05:30' : '2026-10-12T15:29:00+05:30',
      expectedSession: '2026-10-12',
      daysBehind: status('freshness') === 'fail' ? 2 : 0,
    },
    officialDaily: {
      status: 'notAvailable',
      message: 'Not available yet — official daily candles are not fetched.',
    },
  };
}

/** The detail of a row: its checks, the missing minutes of its completeness check and the stored-vs-recomputed buckets. */
export function buildQualityDetail(
  row: QualityInstrumentRow,
  overrides: Partial<QualityInstrumentDetail> = {},
): QualityInstrumentDetail {
  const checks = row.checks === null ? null : buildChecks(row);
  const missing = row.missingMinutes ?? 0;
  const items = sampleBuckets(row.checks?.aggregates === 'fail');
  return {
    instrumentId: row.instrumentId,
    symbol: row.symbol,
    name: row.name,
    type: row.type,
    tracked: true,
    source: row.source,
    firstBar: '2024-04-01T09:15:00+05:30',
    lastBar: '2026-10-12T15:29:00+05:30',
    status: row.status,
    score: row.score,
    barsChecked: row.barsChecked,
    runId: row.runId,
    checkedAt: row.checkedAt,
    checks,
    missing: {
      total: missing,
      truncated: false,
      items: Array.from({ length: Math.min(missing, 500) }, (_, n) => ({
        date: '2026-10-09',
        time: `${String(9 + Math.floor((15 + n) / 60)).padStart(2, '0')}:${String((15 + n) % 60).padStart(2, '0')}`,
      })),
    },
    buckets: {
      computedAt: '2026-10-12T16:00:00+05:30',
      total: items.length,
      differences: items.filter((b) => b.differs).length,
      truncated: false,
      items,
    },
    ...overrides,
  };
}

export const buildQualityRun = (overrides: Partial<QualityRun> = {}): QualityRun => ({
  id: qualityRunId(1),
  trigger: 'daily',
  status: 'succeeded',
  startedAt: '2026-10-12T15:51:00+05:30',
  finishedAt: checkedAt,
  sessions: 5,
  instruments: 4,
  passed: 1,
  warned: 1,
  failed: 2,
  score: 88,
  error: null,
  instrumentId: null,
  jobId: null,
  ...overrides,
});

export const buildQualityPhase = (overrides: Partial<QualityPhase> = {}): QualityPhase => ({
  status: 'succeeded',
  startedAt: '2026-10-12T16:00:00+05:30',
  finishedAt: '2026-10-12T16:01:00+05:30',
  done: 4,
  total: 4,
  message: null,
  code: null,
  runId: qualityRunId(2),
  passed: 2,
  warned: 1,
  failed: 1,
  ...overrides,
});

export const buildQualityGate = (overrides: Partial<QualityGate> = {}): QualityGate => ({
  ok: true,
  blocked: false,
  reasons: [],
  checkedInstruments: 4,
  failedInstruments: 0,
  failedPercent: 0,
  maxFailedPercent: 5,
  lastRunAt: checkedAt,
  ...overrides,
});

export const buildQualityCounts = (overrides: Partial<QualityCounts> = {}): QualityCounts => ({
  tracked: 5,
  checked: 4,
  passed: 1,
  warned: 1,
  failed: 2,
  notChecked: 1,
  score: 88,
  ...overrides,
});

export const buildQualityOverview = (overrides: Partial<QualityOverview> = {}): QualityOverview => ({
  lastRun: buildQualityRun(),
  counts: buildQualityCounts(),
  gate: buildQualityGate(),
  window: { sessions: 5, from: '2026-10-06', to: '2026-10-12' },
  spikeMultiple: 20,
  maxMissingPercent: 1,
  pendingCorporateActions: 0,
  ...overrides,
});
