import { http, HttpResponse } from 'msw';
import type { Schemas } from '@/shared/api';
import { server } from './server';

type Metric = Schemas['MetricView'];
type Account = Schemas['PaperAccountView'];
type Positions = Schemas['PaperPositionList'];
type Position = Schemas['PaperPositionView'];
type Equity = Schemas['PaperEquityView'];
type Reconcile = Schemas['PaperReconcileView'];

export const paperBasis = 'simulated on the official 1-minute bars; no order was placed or exists';
export const paperAccountId = '11111111-1111-4111-8111-111111111111';
export const otherPaperAccountId = '22222222-2222-4222-8222-222222222222';

export const paperMetric = (number: number): Metric => ({ state: 'value', number, reason: null });
export const paperGone = (state: string, reason: string): Metric => ({ state, number: null, reason });

export const buildPaperAccount = (over: Partial<Account> = {}): Account => ({
  accountId: paperAccountId,
  name: 'Paper A',
  epochId: 1,
  capital: 100000,
  capitalSource: 'fixed',
  realised: {
    gross: 1500,
    charges: 100,
    net: 1400,
    basis: 'after the stored sheet; the sheet is not confirmed as the owner’s in this view (ISS-25)',
  },
  equity: paperMetric(101400),
  fundsAvailable: paperMetric(90000),
  openRiskPct: paperMetric(0.8),
  openCount: 1,
  unrealised: paperMetric(250),
  lastSessionResult: 300,
  lastSessionPct: paperMetric(0.3),
  maxDrawdownClosed: { amount: 200, pct: paperMetric(0.2) },
  chargeSheet: { id: 'sheet-1', confirmedOn: null },
  evidence: { closedTrades: 2, openTrades: 1, epochStartedOn: '2026-10-01', lastClosedSession: '2026-10-23' },
  limits: ['limit: no leverage is modelled'],
  ...over,
});

export const buildPaperPosition = (over: Partial<Position> = {}): Position => ({
  symbol: 'RELIANCE',
  side: 'BUY',
  qty: 10,
  entryAt: '2026-10-22T10:00:00+05:30',
  entryPrice: 2450.5,
  stopPrice: 2420,
  mark: { state: 'value', price: 2475, asOf: '2026-10-23', reason: null },
  unrealised: paperMetric(245),
  riskAmount: 305,
  riskPctOfEquity: paperMetric(0.3),
  heldSessions: 2,
  ...over,
});

export const buildPaperPositions = (over: Partial<Positions> = {}): Positions => ({
  basis: paperBasis,
  limits: ['limit: marks are daily closes'],
  accountId: paperAccountId,
  asOfSession: '2026-10-23',
  open: [buildPaperPosition()],
  closed: [
    {
      symbol: 'TCS',
      side: 'SELL',
      qty: 5,
      entryAt: '2026-10-20T10:00:00+05:30',
      exitAt: '2026-10-21T14:00:00+05:30',
      gross: 800,
      charges: 50,
      result: 750,
      r: 1.5,
    },
  ],
  ...over,
});

export const buildPaperEquity = (over: Partial<Equity> = {}): Equity => ({
  basis: paperBasis,
  limits: [],
  accountId: paperAccountId,
  epochId: 1,
  points: [
    { date: '2026-10-21', realised: 750, equity: paperMetric(100750) },
    { date: '2026-10-22', realised: 750, equity: paperMetric(100750) },
    { date: '2026-10-23', realised: 1400, equity: paperMetric(101400) },
  ],
  ...over,
});

export const buildPaperReconcile = (over: Partial<Reconcile> = {}): Reconcile => ({
  basis: paperBasis,
  accountId: paperAccountId,
  epochId: 1,
  status: 'ok',
  differences: [],
  ...over,
});

export interface PaperMount {
  accounts?: Account[];
  positions?: Positions | ((id: string) => Positions);
  reconcile?: Reconcile;
}

/** Serves the four read-only paper endpoints. */
export function mountPaper(opts: PaperMount = {}) {
  server.use(
    http.get('/api/paper/accounts', () =>
      HttpResponse.json({ basis: paperBasis, items: opts.accounts ?? [buildPaperAccount()] }),
    ),
    http.get('/api/paper/accounts/:id/positions', ({ params }) => {
      const id = String(params.id);
      const p = opts.positions;
      return HttpResponse.json(typeof p === 'function' ? p(id) : (p ?? buildPaperPositions({ accountId: id })));
    }),
    http.get('/api/paper/accounts/:id/equity', ({ params }) =>
      HttpResponse.json(buildPaperEquity({ accountId: String(params.id) })),
    ),
    http.get('/api/paper/accounts/:id/reconcile', ({ params }) =>
      HttpResponse.json(opts.reconcile ?? buildPaperReconcile({ accountId: String(params.id) })),
    ),
  );
}
