import { http, HttpResponse } from 'msw';
import type { Schemas } from '@/shared/api';
import { buildEvidence, runKeyA } from './reportFixtures';
import { problemResponse, server } from './server';

type Replay = Schemas['TradeReplayView'];

/** 2026-03-02 (a Monday) at the India time of the minute: epoch seconds. */
export const istEpoch = (hour: number, minute: number): number => Date.UTC(2026, 2, 2, hour, minute) / 1000 - 19_800;

/** Five-minute bars from 09:15 to 10:30 India time (16 bars), opening at `open`. */
export const fiveMinuteBars = (open = 100.5): Replay['chart']['bars'] =>
  Array.from({ length: 16 }, (_, i) => ({
    t: istEpoch(9, 15) + i * 300,
    o: open,
    h: open + 1,
    l: open - 1,
    c: open,
    v: 1000,
  }));

export const buildReplay = (over: Partial<Replay> = {}): Replay => ({
  runKey: runKeyA,
  ordinal: 4,
  openAtEnd: false,
  tradeKey: 'ORB|NSE:AAA|BUY|2026-03-02',
  basis: 'gross',
  trade: {
    strategyId: 'ORB',
    instrumentId: '00000000-0000-0000-0000-00000000000a',
    symbol: 'NSE:AAA',
    side: 'BUY',
    tradeDate: '2026-03-02',
    triggerTimeframe: '15m',
    decisionAt: new Date(istEpoch(9, 59) * 1000).toISOString(),
    entryAt: new Date(istEpoch(10, 0) * 1000).toISOString(),
    entryPrice: 100.5,
    entryFilledPrice: 100.55,
    stopPrice: 98,
    targetPrice: 105.5,
    exitAt: new Date(istEpoch(10, 10) * 1000).toISOString(),
    exitPrice: 105.5,
    exitFilledPrice: 105.45,
    exitReason: 'target',
    rExact: 2,
    rFilled: 1.95,
    slippageR: -0.05,
    maeR: 0.4,
    mfeR: 2.1,
    sessionsHeld: 1,
    sessionsUnseen: 0,
    fills: 'modelled',
    entryFill: 'at_open',
    tickFallback: false,
    basketIds: [],
  },
  legs: [],
  costing: null,
  chart: {
    timeframe: '5m',
    adjusted: true,
    adjustedThrough: null,
    from: new Date(istEpoch(9, 15) * 1000).toISOString(),
    to: new Date(istEpoch(15, 29) * 1000).toISOString(),
    barCount: 16,
    bars: fiveMinuteBars(),
    coherence: { state: 'ok', entryStored: 100.5, entryOnBars: 100.5, detail: 'The bars agree with the stored entry.' },
  },
  features: {
    state: 'unavailable',
    version: 'tf-1',
    reason: 'No decision-time features stored for this trade (run `xd learn features`).',
    items: [],
  },
  evidence: buildEvidence({ n: 1, runKeys: [runKeyA], runs: [] }),
  limits: [
    'A simulated trade on replayed history with modelled fills: the chart shows the bars the trade was simulated on, not a fill you got.',
  ],
  ...over,
});

/** Answers the replay endpoint with `replay` (or a problem) and returns the URLs asked. */
export function replayServer(replay: Replay | { problem: string; code: string; status: number } = buildReplay()) {
  const requests: URL[] = [];
  server.use(
    http.get('/api/reports/trades/:runKey/:ordinal', ({ request }) => {
      requests.push(new URL(request.url));
      return 'problem' in replay
        ? problemResponse({ title: replay.problem, code: replay.code }, replay.status)
        : HttpResponse.json(replay);
    }),
  );
  return requests;
}
