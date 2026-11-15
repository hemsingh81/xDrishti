import type { Schemas } from '@/shared/api';

type Trade = Schemas['SimDayTrade'];
type Bar = Schemas['SimTapeBar'];
type Board = Schemas['SimBoard'];
type Position = Schemas['SimBoardPosition'];

/** The trade fields the board needs: a day's trade, or a position held in from an earlier day. */
export type BoardTrade = Pick<Trade, 'tradeNo' | 'symbol' | 'side' | 'qty' | 'entryAt' | 'entryPrice' | 'stopPrice'> &
  Partial<Pick<Trade, 'exitAt' | 'grossPnl'>>;

const minuteMs = 60_000;
const istOffsetMs = 330 * minuteMs;

/** The start of the minute of an instant, in ms. */
export const minuteOf = (at: string | number) =>
  Math.floor((typeof at === 'number' ? at : Date.parse(at)) / minuteMs) * minuteMs;

const istDay = (ms: number) => new Date(ms + istOffsetMs).toISOString().slice(0, 10);

/** 09:15 India time of the day, in ms. */
export const marketOpenMs = (day: string) => Date.parse(`${day}T09:15:00+05:30`);
/** The last minute of the session (15:29 India time), in ms. */
export const marketCloseMs = (day: string) => Date.parse(`${day}T15:29:00+05:30`);
/** The minutes of the session: 375 (09:15 to 15:29). */
export const sessionMinutes = 375;

/** An instant as the API writes it, in India time (`2026-02-02T09:15:00+05:30`). */
export function istIso(ms: number): string {
  return `${new Date(ms + istOffsetMs).toISOString().slice(0, 19)}+05:30`;
}

/**
 * The live board at the clock minute — the mirror of the backend's pure `SimTape.BoardAt` (ADR 0293). A trade is open once
 * its entry minute is at or before the minute and until its exit minute is; its last price is the close of the last bar of
 * its symbol at or before the minute. Later bars and exits are ignored, so the board never reads past the clock.
 */
export function boardAt(
  minute: number,
  equityOpen: number,
  trades: readonly BoardTrade[],
  bars: readonly Bar[],
): Board {
  const last = new Map<string, { at: number; close: number }>();
  for (const bar of bars) {
    const at = Date.parse(bar.at);
    const seen = last.get(bar.symbol);
    if (at <= minute && (!seen || at > seen.at)) last.set(bar.symbol, { at, close: bar.close });
  }
  const open: Position[] = [];
  let realised = 0;
  for (const t of [...trades].sort((a, b) => a.tradeNo - b.tradeNo)) {
    if (minuteOf(t.entryAt) > minute) continue;
    if (t.exitAt && minuteOf(t.exitAt) <= minute) {
      // an exit of an earlier day is in the equity at the open already
      if (istDay(Date.parse(t.exitAt)) === istDay(minute)) realised += t.grossPnl ?? 0;
      continue;
    }
    const price = last.get(t.symbol)?.close ?? null;
    const sign = t.side.toUpperCase() === 'SELL' ? -1 : 1;
    const runningPnl = price === null ? 0 : (price - t.entryPrice) * t.qty * sign;
    open.push({
      tradeNo: t.tradeNo,
      symbol: t.symbol,
      side: t.side,
      qty: t.qty,
      entryAt: t.entryAt,
      entryPrice: t.entryPrice,
      stop: t.stopPrice,
      lastPrice: price,
      runningPnl,
    });
  }
  const unrealised = open.reduce((sum, p) => sum + p.runningPnl, 0);
  return { at: istIso(minute), open, realised, unrealised, equityOpen, equity: equityOpen + realised };
}

/**
 * The trades of the board: the day's own (from its snapshot) and the positions held in from an earlier day that stay open
 * all day (the snapshot lists only trades entering or exiting on the day, so those are read from a tape page's board).
 */
export function boardTrades(dayTrades: readonly Trade[], heldOpen: readonly Position[]): BoardTrade[] {
  const known = new Set(dayTrades.map((t) => t.tradeNo));
  const held = heldOpen
    .filter((p) => !known.has(p.tradeNo))
    .map((p) => ({ ...p, stopPrice: p.stop, exitAt: null, grossPnl: null }));
  return [...dayTrades, ...held];
}
