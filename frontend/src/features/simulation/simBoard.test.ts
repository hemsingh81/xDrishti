import { describe, expect, it } from 'vitest';
import { api, unwrap } from '@/shared/api';
import { buildSimDay, DAY, dayTrades, SIM, tapeBars, tapeHandler } from '@/test/simDaysFixtures';
import { server } from '@/test/server';
import { boardAt, boardTrades, istIso, marketOpenMs } from './lib/simBoard';

const page = async (to: string) =>
  unwrap(
    await api.GET('/api/sim/days/{id}/days/{date}/tape', {
      params: { path: { id: SIM, date: DAY }, query: { from: `${DAY}T09:15:00+05:30`, to } },
    }),
  );

const minute = (hhmm: string) => Date.parse(`${DAY}T${hhmm}:00+05:30`);

describe('boardAt (the mirror of SimTape.BoardAt)', () => {
  it('equals the tape page board at each minute, and the end-of-day board equals the closing equity', async () => {
    server.use(tapeHandler());
    const first = await page(istIso(marketOpenMs(DAY)));
    const trades = boardTrades(dayTrades, first.board.open);
    for (const hhmm of ['09:15', '09:16', '09:17', '09:18', '09:44', '15:29']) {
      const served = await page(istIso(minute(hhmm)));
      const mine = boardAt(minute(hhmm), served.board.equityOpen, trades, served.bars);
      expect(mine, hhmm).toEqual(served.board);
    }
    const end = boardAt(minute('15:29'), first.board.equityOpen, trades, tapeBars);
    expect(end.equity).toBe(buildSimDay().equityClose);
  });

  it('never reads a bar or an exit after the minute', () => {
    // every bar of the day is given; the board at 09:16 still sees only what is at or before it
    const board = boardAt(minute('09:16'), 1_000_000, dayTrades, tapeBars);
    expect(board.open.map((p) => [p.symbol, p.lastPrice])).toEqual([['RELIANCE', 101]]);
    expect(board.realised).toBe(0);
    expect(board.equity).toBe(1_000_000);
  });

  it('shows a position with no bar yet at a running P&L of 0', () => {
    const board = boardAt(minute('09:17'), 1_000_000, dayTrades, []);
    expect(board.open.map((p) => [p.symbol, p.lastPrice, p.runningPnl])).toEqual([
      ['RELIANCE', null, 0],
      ['TCS', null, 0],
    ]);
  });
});
