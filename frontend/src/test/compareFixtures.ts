import { http, HttpResponse } from 'msw';
import type { Schemas } from '@/shared/api';
import { paperBasis } from './paperFixtures';
import { server } from './server';

type View = Schemas['BacktestVsPaperView'];
type Metric = Schemas['MetricView'];

const value = (number: number): Metric => ({ state: 'value', number, reason: null });
const gone = (state: string, reason: string): Metric => ({ state, number: null, reason });

/** A 60-trade comparison: mean +0.2R inside the band, interval including 0, both statistics met. */
export const buildCompare = (over: Partial<View> = {}): View => ({
  epochId: 1,
  n: 60,
  counts: { counted: 60, reserve: 2, straddle: 1, open: 3, otherEpochs: 4 },
  basis: 'net',
  chargeSheet: { id: 'flat-10', confirmedOn: '2026-10-30' },
  paper: { mean: value(0.2), ci90: { lo: value(-0.115), hi: value(0.515) }, maxDrawdownR: value(4.5) },
  band: {
    p05: value(-0.3),
    p50: value(0),
    p95: value(0.3),
    rank: value(86),
    position: 'within',
    draws: 10000,
    seed: '123',
  },
  drawdown: { p50: value(4), p90: value(6), p95: value(7), position: 'within' },
  criteria: {
    expectancy: { verdict: 'met', label: 'paper mean +0.200R is above 0 (point estimate; 90% interval includes 0)' },
    drawdown: { verdict: 'met', label: 'paper largest fall 4.500R, backtest 90th percentile 6.000R: within' },
  },
  earlyWarning: null,
  cells: [
    {
      cellKey: 'ORB|15m|BUY|intraday|FIXED_RR|*',
      paperN: 60,
      paperMean: value(0.2),
      referenceN: 120,
      referenceMean: value(0.05),
      state: 'ok',
      reason: null,
    },
  ],
  fan: [
    { index: 1, p05: -1, p50: 0, p95: 2, paper: 1 },
    { index: 2, p05: -2, p50: 0, p95: 3, paper: 0.5 },
    { index: 3, p05: -2.5, p50: 0.1, p95: 3.5, paper: 1.5 },
  ],
  sensitivityAllProposals: {
    label: 'simulated on plan prices, all proposals',
    n: 80,
    mean: value(0.12),
  },
  evidence: {
    epochStart: '2026-10-01',
    firstExit: '2026-10-02',
    lastExit: '2026-10-23',
    snapshotKey: 'snap',
    referenceAsOf: '2026-09-30',
    runKeys: ['run-a'],
    statement: paperBasis,
  },
  limits: ['limit: the reference is simulated'],
  ...over,
});

export const buildThinCompare = (): View =>
  buildCompare({
    n: 12,
    paper: {
      mean: gone('insufficient', '12 counted trades; at least 30 are needed'),
      ci90: {
        lo: gone('insufficient', '12 counted trades; at least 30 are needed'),
        hi: gone('insufficient', '12 counted trades; at least 30 are needed'),
      },
      maxDrawdownR: gone('insufficient', '12 counted trades; at least 30 are needed'),
    },
    band: {
      p05: gone('insufficient', '12 counted trades; at least 30 are needed'),
      p50: gone('insufficient', 'x'),
      p95: gone('insufficient', 'x'),
      rank: gone('insufficient', 'x'),
      position: null,
      draws: 10000,
      seed: null,
    },
    drawdown: {
      p50: gone('insufficient', 'x'),
      p90: gone('insufficient', 'x'),
      p95: gone('insufficient', 'x'),
      position: null,
    },
    criteria: {
      expectancy: { verdict: 'not_evaluable', label: 'not evaluable: 12 counted trades, 60 are needed' },
      drawdown: { verdict: 'not_evaluable', label: 'not evaluable: 12 counted trades, 60 are needed' },
    },
    fan: [],
  });

export function mountCompare(view: View = buildCompare()) {
  server.use(http.get('/api/paper/backtest-vs-paper', () => HttpResponse.json(view)));
}
