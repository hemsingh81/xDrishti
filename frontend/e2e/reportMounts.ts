import './shim';
import { http, HttpResponse } from 'msw';
import type { Schemas } from '../src/shared/api';
import { buildEvidence, buildRunsView, rankedWhatWorks, runKeyA } from '../src/test/reportFixtures';
import { reportServer } from '../src/test/reportScreen';
import { problemResponse, server } from '../src/test/server';

// Fixture mounts of the Reports screens for the browser runner (P8-16).
export const reportBase = () => {
  reportServer({ whatWorks: rankedWhatWorks() });
  server.use(
    http.get('/api/reports/benchmarks', () => problemResponse({ title: 'Not found', code: 'reports.not_found' }, 404)),
  );
};
export const reportEmpty = () => {
  reportServer({ runs: buildRunsView([]) });
  server.use(
    http.get('/api/reports/benchmarks', () => problemResponse({ title: 'Not found', code: 'reports.not_found' }, 404)),
  );
};
const tradeLine = (ordinal: number): Schemas['TradeLineView'] => ({
  runKey: runKeyA,
  tradeKey: `ORB|NSE:S${String(ordinal)}|BUY|2026-03-02`,
  strategy: 'ORB',
  timeframe: '15m',
  side: 'BUY',
  symbol: `S${String(ordinal)}`,
  basketIds: [],
  entryDate: '2026-03-02',
  exitDate: '2026-03-03',
  r: ordinal % 2 === 0 ? 1.5 : -1,
  sessionsHeld: 1,
  style: 'intraday',
  exit: 'fixed_r',
  provisional: false,
  ordinal,
});
export const mountTrades = (count: number) => () => {
  reportBase();
  const items = Array.from({ length: count }, (_, i) => tradeLine(i + 1));
  server.use(
    http.get('/api/reports/trades', () =>
      HttpResponse.json({
        items,
        total: count,
        nextCursor: null,
        evidence: buildEvidence({ n: count }),
      } satisfies Schemas['TradesView']),
    ),
  );
};
