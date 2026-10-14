import type { PricesChanged } from '@/shared/realtime';
import { applyPrices, LiveTickStore, newestLiveTick, noTicks } from './liveTicks';

const message = (instruments: PricesChanged['instruments'], asOf = '2026-10-14T05:06:00Z'): PricesChanged => ({
  asOf,
  instruments,
});
const price = (id: string, lastPrice: number, priceAt: string, source = 'live') => ({
  instrumentId: id,
  lastPrice,
  priceAt,
  source,
});

describe('pushed prices', () => {
  it('keep the newest price time per instrument and ignore an older one', () => {
    const first = applyPrices(noTicks, message([price('a', 10, '2026-10-14T05:05:10Z')]), 0);
    const older = applyPrices(first, message([price('a', 99, '2026-10-14T05:05:09Z')]), 0);
    const newer = applyPrices(older, message([price('a', 11, '2026-10-14T05:05:11Z')]), 0);

    expect(older.ticks.get('a')?.price).toBe(10);
    expect(newer.ticks.get('a')?.price).toBe(11);
  });

  it('replace a price of the same second by another, and keep the state when nothing is new', () => {
    const first = applyPrices(noTicks, message([price('a', 10, '2026-10-14T05:05:10Z')]), 0);
    const same = applyPrices(first, message([price('a', 10, '2026-10-14T05:05:10Z')]), 0);
    const changed = applyPrices(first, message([price('a', 12, '2026-10-14T05:05:10Z')]), 0);

    expect(same).toBe(first);
    expect(changed.ticks.get('a')?.price).toBe(12);
  });

  it('skip a price without a usable value or time', () => {
    const state = applyPrices(
      noTicks,
      message([price('a', 0, '2026-10-14T05:05:10Z'), price('b', 5, 'not a time')]),
      0,
    );
    expect(state.ticks.size).toBe(0);
  });

  it('measure the server clock against the browser clock', () => {
    const state = applyPrices(noTicks, message([], '2026-10-14T05:06:00Z'), Date.parse('2026-10-14T05:06:05Z'));
    expect(state.skewMs).toBe(-5000);
  });

  it('give the newest live tick of some instruments, not counting quotes and syncs', () => {
    const state = applyPrices(
      noTicks,
      message([
        price('a', 1, '2026-10-14T05:05:10Z'),
        price('b', 1, '2026-10-14T05:05:20Z'),
        price('c', 1, '2026-10-14T05:05:30Z', 'quote'),
      ]),
      0,
    );
    expect(newestLiveTick(state.ticks, ['a', 'b', 'c', 'missing'])).toBe(Date.parse('2026-10-14T05:05:20Z'));
    expect(newestLiveTick(state.ticks, ['c'])).toBeNull();
  });
});

describe('LiveTickStore', () => {
  it('tells its listeners only when something was new', () => {
    const store = new LiveTickStore();
    const listener = vi.fn();
    const off = store.subscribe(listener);

    store.apply(message([price('a', 1, '2026-10-14T05:05:10Z')]), 0);
    store.apply(message([price('a', 1, '2026-10-14T05:05:10Z')], '2026-10-14T05:06:00Z'), 0);
    expect(listener).toHaveBeenCalledTimes(1);
    expect(store.getSnapshot().ticks.get('a')?.price).toBe(1);

    off();
    store.apply(message([price('a', 2, '2026-10-14T05:05:11Z')]), 0);
    expect(listener).toHaveBeenCalledTimes(1);
  });
});
