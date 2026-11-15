import { http, HttpResponse } from 'msw';
import type { Schemas } from '@/shared/api';
import { problemResponse } from './server';

/** SR-04 day-by-day simulation fixtures: one settled day (2026-02-02) with a hand-worked tape and its boards. */
export const SIM = '44444444-4444-4444-4444-444444444444';
export const DAY = '2026-02-02';
export const COSTS = 'gross: before costs (costs are gross for ever, ADR 0292)';

type View = Schemas['SimDaysView'];
type Snapshot = Schemas['SimDaySnapshotView'];
type Board = Schemas['SimBoard'];
type Bar = Schemas['SimTapeBar'];
type Event = Schemas['SimTapeEvent'];

const at = (hhmm: string, day = DAY) => `${day}T${hhmm}:00+05:30`;

export const buildSimDays = (over: Partial<View> = {}): View => ({
  id: SIM,
  start: DAY,
  end: '2026-02-06',
  requestedEnd: '2026-02-06',
  cursor: DAY,
  phase: 'settled',
  auto: false,
  mode: 'learning',
  capital: 1_000_000,
  equity: 1_000_000,
  settledEquity: 1_000_040,
  daysDone: 0,
  daysTotal: 5,
  holdOutNote: null,
  waiting: null,
  failReason: null,
  hindsight: [{ code: 'versions_now', text: "Today's active versions are used for every day." }],
  costsLabel: COSTS,
  speedSecondsPerMinute: 1,
  createdAt: '2026-11-15T03:00:00Z',
  updatedAt: '2026-11-15T03:00:00Z',
  hasNewSessions: false,
  ...over,
});

const trade = (over: Partial<Schemas['SimDayTrade']>): Schemas['SimDayTrade'] => ({
  tradeNo: 0,
  symbol: '',
  side: 'BUY',
  cellKey: 'c1',
  qty: 1,
  entrySession: DAY,
  entryAt: at('09:15'),
  entryPrice: 0,
  stopPrice: 0,
  swing: false,
  exitSession: null,
  exitAt: null,
  grossPnl: null,
  r: null,
  instrumentId: null,
  exitPrice: null,
  exitReason: null,
  ...over,
});

/** The day's own trades: RELIANCE in and out (+40), TCS short and open. INFY (trade 1) is held in from 30 Jan, open all day. */
export const dayTrades = [
  trade({
    tradeNo: 2,
    symbol: 'RELIANCE',
    qty: 10,
    entryAt: at('09:16'),
    entryPrice: 100,
    stopPrice: 95,
    exitSession: DAY,
    exitAt: at('09:18'),
    grossPnl: 40,
    exitPrice: 104,
    exitReason: 'target',
  }),
  trade({ tradeNo: 3, symbol: 'TCS', side: 'SELL', qty: 5, entryAt: at('09:17'), entryPrice: 200, stopPrice: 205 }),
];

export const buildSimDay = (over: Partial<Snapshot> = {}): Snapshot => ({
  runId: SIM,
  session: DAY,
  stored: false,
  picks: [{ symbol: 'HDFCBANK', side: 'BUY', cellKey: 'c1', rank: 1 }],
  blocked: [{ symbol: 'WIPRO', cellKey: 'c1', reason: 'risk_cap', detail: 'max open positions' }],
  fills: [],
  trades: dayTrades,
  equityClose: 1_000_040,
  drawdownPct: null,
  openPositions: 2,
  notes: [],
  resultHash: 'h',
  hindsight: [],
  costsLabel: COSTS,
  ...over,
});

const bar = (symbol: string, hhmm: string, close: number): Bar => ({
  symbol,
  at: at(hhmm),
  open: close,
  high: close,
  low: close,
  close,
  volume: 100,
});

export const tapeBars: Bar[] = [
  bar('INFY', '09:15', 51),
  bar('RELIANCE', '09:15', 99),
  bar('RELIANCE', '09:16', 101),
  bar('RELIANCE', '09:17', 103),
  bar('TCS', '09:17', 199),
  bar('RELIANCE', '09:18', 104),
  bar('TCS', '09:18', 198),
  bar('INFY', '09:20', 52),
  bar('INFY', '15:29', 55),
  bar('RELIANCE', '15:29', 105),
  bar('TCS', '15:29', 190),
];

const event = (over: Partial<Event>): Event => ({
  at: at('09:15'),
  kind: 'entry',
  tradeNo: 0,
  symbol: '',
  side: 'BUY',
  qty: 1,
  price: null,
  stop: 0,
  grossPnl: null,
  reason: null,
  ...over,
});

export const tapeEvents: Event[] = [
  event({ at: at('09:16'), tradeNo: 2, symbol: 'RELIANCE', qty: 10, price: 100, stop: 95 }),
  event({ at: at('09:17'), tradeNo: 3, symbol: 'TCS', side: 'SELL', qty: 5, price: 200, stop: 205 }),
  event({
    at: at('09:18'),
    kind: 'exit',
    tradeNo: 2,
    symbol: 'RELIANCE',
    qty: 10,
    price: 104,
    stop: 95,
    grossPnl: 40,
    reason: 'target',
  }),
];

const infy = (last: number) => ({
  tradeNo: 1,
  symbol: 'INFY',
  side: 'BUY',
  qty: 2,
  entryAt: '2026-01-30T10:00:00+05:30',
  entryPrice: 50,
  stop: 45,
  lastPrice: last,
  runningPnl: (last - 50) * 2,
});
const reliance = (last: number) => ({
  tradeNo: 2,
  symbol: 'RELIANCE',
  side: 'BUY',
  qty: 10,
  entryAt: at('09:16'),
  entryPrice: 100,
  stop: 95,
  lastPrice: last,
  runningPnl: (last - 100) * 10,
});
const tcs = (last: number) => ({
  tradeNo: 3,
  symbol: 'TCS',
  side: 'SELL',
  qty: 5,
  entryAt: at('09:17'),
  entryPrice: 200,
  stop: 205,
  lastPrice: last,
  runningPnl: (200 - last) * 5,
});
const board = (hhmm: string, open: Board['open'], realised: number): Board => ({
  at: at(hhmm),
  open,
  realised,
  unrealised: open.reduce((s, p) => s + p.runningPnl, 0),
  equityOpen: 1_000_000,
  equity: 1_000_000 + realised,
});

/** The backend's boards at these minutes, worked by hand from the bars and trades above (the server side of the parity). */
export const serverBoards: Record<string, Board> = {
  '09:15': board('09:15', [infy(51)], 0),
  '09:16': board('09:16', [infy(51), reliance(101)], 0),
  '09:17': board('09:17', [infy(51), reliance(103), tcs(199)], 0),
  '09:18': board('09:18', [infy(51), tcs(198)], 40),
  '09:44': board('09:44', [infy(52), tcs(198)], 40),
  '15:29': board('15:29', [infy(55), tcs(190)], 40),
};

const hhmmOf = (iso: string) => {
  const d = new Date(Date.parse(iso) + 330 * 60_000);
  return d.toISOString().slice(11, 16);
};

/** The tape endpoint: the bars and events in [from, to], and the board at `to` (a minute not worked above reads the latest worked one before it). */
export function tapeHandler(asked: string[] = []) {
  return http.get('/api/sim/days/:id/days/:date/tape', ({ request }) => {
    const url = new URL(request.url);
    const from = url.searchParams.get('from') ?? at('09:15');
    const to = url.searchParams.get('to') ?? at('15:29');
    asked.push(`${hhmmOf(from)}-${hhmmOf(to)}`);
    const lo = Date.parse(from);
    const hi = Date.parse(to);
    const inRange = (x: string) => Date.parse(x) >= lo && Date.parse(x) <= hi;
    const key =
      Object.keys(serverBoards)
        .filter((k) => k <= hhmmOf(to))
        .at(-1) ?? '09:15';
    const worked = serverBoards[key];
    if (!worked) return problemResponse({ code: 'sim.invalid', detail: 'no board' }, 422);
    return HttpResponse.json({
      runId: SIM,
      session: DAY,
      from,
      to,
      marketOpen: at('09:15'),
      marketClose: at('15:29'),
      symbols: ['INFY', 'RELIANCE', 'TCS'],
      bars: tapeBars.filter((b) => inRange(b.at)),
      events: tapeEvents.filter((e) => inRange(e.at)),
      board: { ...worked, at: to },
      costsLabel: COSTS,
    } satisfies Schemas['SimDayTapeView']);
  });
}

export const simSettings: Schemas['SimDaysSettingsView'] = {
  defaultCapital: 2_500_000,
  speedSecondsPerMinute: 1,
  autoContinue: false,
};

export const listItem = (
  view: View,
  over: Partial<Schemas['SimDaysListItemView']> = {},
): Schemas['SimDaysListItemView'] => ({
  id: view.id,
  phase: view.phase,
  active: !['complete', 'cancelled', 'error'].includes(view.phase),
  auto: view.auto,
  mode: view.mode,
  scope: { baskets: 1, instruments: 0 },
  start: view.start,
  end: view.end,
  cursor: view.cursor,
  capital: view.capital,
  equity: view.equity,
  daysDone: view.daysDone,
  createdAt: view.createdAt,
  ...over,
});

/** The settings, the list of runs, the status, the stored-days list and the cursor day; other days are 404 (not stored). */
export function simDaysServer(
  view: View = buildSimDays(),
  day: Snapshot | null = buildSimDay(),
  stored: Schemas['SimDayListItemView'][] = [],
) {
  return [
    http.get('/api/sim/days/settings', () => HttpResponse.json(simSettings)),
    http.get('/api/sim/days', () => HttpResponse.json({ items: [listItem(view)], nextCursor: null })),
    http.get('/api/sim/days/:id/days', () => HttpResponse.json({ items: stored, nextCursor: null })),
    http.get('/api/sim/days/:id', () => HttpResponse.json(view)),
    http.get('/api/sim/days/:id/days/:date', ({ params }) =>
      day && params.date === day.session
        ? HttpResponse.json(day)
        : problemResponse({ code: 'sim.day_not_found', detail: 'No such day.' }, 404),
    ),
    tapeHandler(),
  ];
}
