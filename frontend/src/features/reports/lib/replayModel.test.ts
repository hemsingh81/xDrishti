import { describe, expect, it } from 'vitest';
import { buildReplay, istEpoch } from '@/test/replayFixtures';
import { gapNote, largerTimeframe, replayEvents, replayLines } from './replayModel';

const iso = (h: number, m: number) => new Date(istEpoch(h, m) * 1000).toISOString();

describe('replay model', () => {
  it('draws entry, stop and target with their text on the axis, and no target line without a target', () => {
    expect(replayLines(buildReplay()).map((l) => l.title)).toEqual(['Entry 100.50', 'Stop 98.00', 'Target 105.50']);
    const none = buildReplay();
    none.trade.targetPrice = null;
    expect(replayLines(none).map((l) => l.kind)).toEqual(['entry', 'stop']);
  });

  it('a trade without legs has its entry and its exit', () => {
    const events = replayEvents(buildReplay());
    expect(events.map((e) => [e.kind, e.price, e.share])).toEqual([
      ['entry', 100.5, 1],
      ['exit', 105.5, 1],
    ]);
  });

  it('a trade with legs has one event per leg, in order, each with its share and reason', () => {
    const view = buildReplay({
      legs: [
        { at: iso(10, 0), kind: 'entry', price: 100.5, filledPrice: 100.55, share: 1, reason: '' },
        { at: iso(10, 5), kind: 'add', price: 101, filledPrice: 101.05, share: 0.5, reason: '' },
        { at: iso(10, 10), kind: 'exit', price: 103, filledPrice: 102.95, share: 0.75, reason: 'partial_1r' },
        { at: iso(10, 20), kind: 'exit', price: 105.5, filledPrice: 105.45, share: 0.75, reason: 'Target' },
      ],
    });
    const events = replayEvents(view);
    expect(events.map((e) => e.kind)).toEqual(['entry', 'add', 'exit', 'exit']);
    expect(events[2]?.reason).toBe('partial_1r');
    expect(events[1]?.text).toBe('Add 50%');
    expect(events.map((e) => e.t)).toEqual([...events.map((e) => e.t)].sort((a, b) => a - b));
  });

  it('announces an event with its kind, time, price and share', () => {
    const view = buildReplay();
    view.trade.exitAt = iso(14, 35);
    view.trade.exitPrice = 412.3;
    expect(replayEvents(view)[1]?.announce).toBe('Exit, 14:35, price 412.30, share 100%');
  });

  it('says a gap through the stop in words and keeps the exit at its own price', () => {
    const view = buildReplay();
    view.trade.stopPrice = 415;
    view.trade.exitPrice = 412.3;
    view.trade.exitReason = 'stop';
    expect(gapNote(view)).toBe('exit at 412.30, gap through the stop 415.00');
    const exit = replayEvents(view)[1];
    expect(exit?.price).toBe(412.3);
    expect(exit?.note).toBe('exit at 412.30, gap through the stop 415.00');
    expect(replayLines(view).find((l) => l.kind === 'stop')?.price).toBe(415);
    view.trade.exitPrice = 415;
    expect(gapNote(view)).toBeNull();
  });

  it('offers the next larger timeframe', () => {
    expect(largerTimeframe('1m')).toBe('5m');
    expect(largerTimeframe('1D')).toBeNull();
  });
});
