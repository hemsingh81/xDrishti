import type { HoldingGroup, HoldingRow } from '@/shared/api';
import { buildSummary } from '@/test/fixtures';
import {
  groupsOf,
  rowOf,
  summaryOf,
  writePrice,
  type ModelAccountIncome,
  type ModelDimension,
  type ModelHolding,
  type ModelLot,
} from '@/test/serverModel';
import { applyTicks } from './liveFigures';
import type { LiveTicks } from './liveTicks';
import { liveGroups, liveSummary, membershipOf } from './liveTotals';

/**
 * Many more random portfolios and pushed prices than the golden file has, against `@/test/serverModel` (a TypeScript model
 * of the server's calculators in whole lots and paise), compared exactly (four decimals on both sides). The authority on what
 * the server computes is `liveGolden.test.ts` (fixtures written by the real C# calculators); this file widens its coverage
 * and keeps the model honest: if the two ever disagree, the golden file wins.
 */

// A small deterministic generator, so a failure names the seed that reproduces it.
function generator(seed: number) {
  let state = seed >>> 0;
  const next = () => {
    state = (state + 0x6d2b79f5) >>> 0;
    let t = state;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4_294_967_296;
  };
  return {
    next,
    int: (min: number, max: number) => min + Math.floor(next() * (max - min + 1)),
    chance: (p: number) => next() < p,
    pick: <T>(items: readonly T[]): T => items[Math.floor(next() * items.length)] as T,
  };
}
type Random = ReturnType<typeof generator>;

const minute = 60_000;
const today = Date.parse('2026-10-14T09:15:00+05:30');
const yesterday = Date.parse('2026-10-13T15:30:00+05:30');
const accounts = ['a1', 'a2', 'a3'];
const buckets = ['b1', 'b2'];
const sectors = ['Energy', 'Banks', 'IT', 'Unclassified'];
const uuid = (kind: string, n: number) => `00000000-0000-4000-8000-${kind}${String(n).padStart(11 - kind.length, '0')}`;

interface InstrumentState {
  pricePaise: number;
  priceAt: number | null;
  source: ModelHolding['source'];
  previousClosePaise: number | null;
  previousCloseAt: number | null;
}

/** A price state a real history can leave behind: a sync, a quote (which wrote the close with its price) or a live tick. */
function instrumentState(r: Random): InstrumentState {
  if (r.chance(0.15)) {
    return { pricePaise: 0, priceAt: null, source: 'sync', previousClosePaise: null, previousCloseAt: null };
  }
  const pricePaise = r.int(5_000, 800_000);
  const previousClosePaise = Math.max(100, Math.round(pricePaise * (0.9 + r.next() * 0.2)));
  const kind = r.pick(['sync', 'quote', 'live', 'live-old-close'] as const);
  const at = today + r.int(1, 30) * minute;
  switch (kind) {
    case 'sync':
      return {
        pricePaise,
        priceAt: yesterday - r.int(0, 600) * minute,
        source: 'sync',
        previousClosePaise: r.chance(0.5) ? previousClosePaise : null,
        previousCloseAt: null,
      };
    case 'quote':
      return { pricePaise, priceAt: at, source: 'quote', previousClosePaise, previousCloseAt: at };
    case 'live':
      return {
        pricePaise,
        priceAt: at,
        source: 'live',
        previousClosePaise,
        previousCloseAt: at - r.int(0, 20) * minute,
      };
    case 'live-old-close':
      return { pricePaise, priceAt: at, source: 'live', previousClosePaise, previousCloseAt: yesterday };
  }
}

function holdings(r: Random): ModelHolding[] {
  const instruments = Array.from({ length: r.int(2, 12) }, () => instrumentState(r));
  return Array.from({ length: r.int(3, 30) }, (_, n): ModelHolding => {
    const closed = r.chance(0.1);
    const lots: ModelLot[] = Array.from({ length: r.int(1, 3) }, () => ({
      qty: r.int(1, 200),
      unitCostPaise: r.int(5_000, 600_000),
      closed,
      closePricePaise: closed ? r.int(5_000, 600_000) : 0,
    }));
    if (!closed && r.chance(0.3))
      lots.push({ qty: r.int(1, 20), unitCostPaise: 10_000, closed: true, closePricePaise: 12_000 });
    const mapped = r.chance(0.9);
    const index = r.int(0, instruments.length - 1);
    const state = mapped ? instruments[index]! : instrumentState(r);
    const openQty = lots.filter((l) => !l.closed).reduce((sum, l) => sum + l.qty, 0);
    return {
      id: uuid('11', n),
      accountId: uuid('aa', accounts.indexOf(r.pick(accounts))),
      bucketId: uuid('bb', buckets.indexOf(r.pick(buckets))),
      sector: r.pick(sectors),
      instrumentId: mapped ? uuid('ee', index) : null,
      // The broker's quantity is the open lots' quantity; sometimes it is not (for a priced holding), which only the day change may notice.
      qty: closed ? 0 : state.pricePaise > 0 && r.chance(0.2) ? openQty + r.int(1, 5) : openQty,
      status: closed ? 'closed' : 'open',
      lots,
      ...state,
    };
  });
}

/**
 * Dividends (to the paisa) on some holdings, charges of trades (four decimals) on some, and, for some accounts, dividends of
 * no holding and costs of the ledger (a refund now and then): the world of the backend's golden generator, drawn from a
 * generator of its own so the holdings and prices of a seed stay what they were.
 */
function income(seed: number, held: readonly ModelHolding[]) {
  const r = generator(seed + 100_000);
  const holdings = held.map((h): ModelHolding => {
    const dividends4 = r.chance(0.45) ? r.int(1, 3) * r.int(1, 5_000_000) * 100 : 0;
    const charges4 = r.chance(0.5) ? r.int(1, 3) * r.int(1, 400_000) : 0;
    return { ...h, dividends4, charges4 };
  });
  const accountsIncome: ModelAccountIncome[] = accounts.flatMap((_, n) => {
    const dividends4 = r.chance(0.3) ? r.int(1, 100_000) * 100 : 0;
    const charges4 = r.chance(0.25) ? r.int(1, 50_000) : 0;
    const ledger4 = r.chance(0.6) ? r.int(-2_000, 80_000) * 100 : 0;
    const both = { accountId: uuid('aa', n), dividends4, charges4: charges4 + ledger4 };
    return both.dividends4 !== 0 || both.charges4 !== 0 ? [both] : [];
  });
  return { holdings, accountsIncome };
}

interface Push {
  instrumentId: string;
  pricePaise: number;
  at: number;
  source: ModelHolding['source'];
}

/** Prices pushed during the session: ticks, and sometimes a quote as the instrument's last word (a quote after a tick would also move the close). */
function pushes(r: Random, holds: readonly ModelHolding[]): Push[] {
  const ids = [...new Set(holds.flatMap((h) => (h.instrumentId === null ? [] : [h.instrumentId])))];
  return ids.flatMap((instrumentId): Push[] => {
    if (r.chance(0.3)) return [];
    const known = holds.find((h) => h.instrumentId === instrumentId);
    const base = known && known.pricePaise > 0 ? known.pricePaise : 100_000;
    const count = r.int(1, 3);
    let at = Math.max(known?.priceAt ?? today, today) + r.int(0, 5) * minute;
    return Array.from({ length: count }, (_, n): Push => {
      at += r.int(0, 3) * 1000 + (r.chance(0.2) ? 0 : 1000);
      const last = n === count - 1;
      const quote = last && (known?.previousClosePaise ?? null) !== null && r.chance(0.2);
      return {
        instrumentId,
        pricePaise: Math.max(5, Math.round(base * (0.95 + r.next() * 0.1))),
        at,
        source: quote ? 'quote' : 'live',
      };
    });
  });
}

/** The browser's ticks: the newest push per instrument (the store keeps only that). */
function ticksOf(list: readonly Push[]): LiveTicks {
  const map = new Map<string, { price: number; at: number; atIso: string; source: string }>();
  for (const p of list) {
    const known = map.get(p.instrumentId);
    if (known && p.at < known.at) continue;
    map.set(p.instrumentId, {
      price: p.pricePaise / 100,
      at: p.at,
      atIso: new Date(p.at).toISOString(),
      source: p.source,
    });
  }
  return map;
}

/** Exactly equal: both sides round money and percentages to four decimals, halves away from zero, as the API does. */
const same = (actual: number | null, expected: number | null) => {
  expect(actual).toBe(expected);
};

function expectRow(actual: HoldingRow, expected: HoldingRow) {
  expect(actual.income).toEqual(expected.income);
  expect(actual.lastPrice).toBe(expected.lastPrice);
  expect(actual.priceAt).toBe(expected.priceAt);
  expect(actual.priceSource).toBe(expected.priceSource);
  expect(actual.priced).toBe(expected.priced);
  same(actual.returns.value, expected.returns.value);
  same(actual.returns.pnl, expected.returns.pnl);
  same(actual.returns.pnlPct, expected.returns.pnlPct);
  same(actual.dayChange, expected.dayChange);
  same(actual.dayChangePct, expected.dayChangePct);
}

function expectGroup(actual: HoldingGroup, expected: HoldingGroup) {
  expect(actual.income).toEqual(expected.income);
  expect(actual.holdings).toBe(expected.holdings);
  expect(actual.unpriced).toBe(expected.unpriced);
  expect(actual.dayChangeExcluded).toBe(expected.dayChangeExcluded);
  same(actual.invested, expected.invested);
  same(actual.value, expected.value);
  same(actual.pnl, expected.pnl);
  same(actual.pnlPct, expected.pnlPct);
  same(actual.dayChange, expected.dayChange);
  same(actual.dayChangePct, expected.dayChangePct);
  same(actual.weightPct, expected.weightPct);
}

describe('live recompute equals the server for the same prices', () => {
  const seeds = Array.from({ length: 150 }, (_, n) => n + 1);

  it.each(seeds)('rows, tiles and groups (seed %i)', (seed) => {
    const r = generator(seed);
    const { holdings: before, accountsIncome } = income(seed, holdings(r));
    const sent = pushes(r, before);
    const after = sent.reduce((state, p) => writePrice(state, p.instrumentId, p.pricePaise, p.at, p.source), before);

    // What the browser has read (before the pushes), and what it makes of the pushes.
    const readRows = before.map(rowOf);
    const live = applyTicks(readRows, ticksOf(sent));
    const open = live.filter((row) => row.status === 'open');

    after.map(rowOf).forEach((expected, index) => {
      expectRow(live[index]!, expected);
    });

    const summary = liveSummary(summaryOf(before, undefined, accountsIncome), open);
    expect(summary).toBeDefined();
    const expectedSummary = summaryOf(after, undefined, accountsIncome);
    expect(summary?.income).toEqual(expectedSummary.income);
    same(summary?.returns.invested ?? null, expectedSummary.returns.invested);
    same(summary?.returns.value ?? null, expectedSummary.returns.value);
    same(summary?.returns.pnl ?? null, expectedSummary.returns.pnl);
    same(summary?.returns.pnlPct ?? null, expectedSummary.returns.pnlPct);
    same(summary?.dayChange ?? null, expectedSummary.dayChange);
    same(summary?.dayChangePct ?? null, expectedSummary.dayChangePct);
    expect(summary?.dayChangeExcluded).toBe(expectedSummary.dayChangeExcluded);
    expect(summary?.unpricedCount).toBe(expectedSummary.unpricedCount);
    expect(summary?.pricesAsOf).toEqual(expectedSummary.pricesAsOf);

    // The groups of the status "all" (closed holdings included) and of the status "open".
    for (const statusOpen of [false, true]) {
      const scope = (state: readonly ModelHolding[]) => (statusOpen ? state.filter((h) => h.status === 'open') : state);
      for (const by of ['account', 'bucket', 'sector'] as const satisfies readonly ModelDimension[]) {
        const lists = sectors.map(
          (s) =>
            [
              s,
              scope(before)
                .filter((h) => h.status === 'open' && h.sector === s)
                .map((h) => h.id),
            ] as const,
        );
        const membership = membershipOf(by, lists);
        expect(membership).toBeDefined();
        const groups = liveGroups(groupsOf(scope(before), by, accountsIncome), open, membership!, statusOpen);
        expect(groups).toBeDefined();
        const expected = groupsOf(scope(after), by, accountsIncome);
        expect(groups?.groups.map((g) => g.key)).toEqual(expected.groups.map((g) => g.key));
        groups?.groups.forEach((group, index) => {
          expectGroup(group, expected.groups[index]!);
        });
        expectGroup(groups?.total as unknown as HoldingGroup, expected.total as unknown as HoldingGroup);
      }
    }
  });
});

describe('the generated cases are not trivial', () => {
  it('move prices and the net total return, count and leave out day changes, and include unpriced and unmapped holdings and Unallocated rows', () => {
    let moved = 0;
    let counted = 0;
    let leftOut = 0;
    let unpriced = 0;
    let unmapped = 0;
    let netMoved = 0;
    let withUnallocated = 0;
    for (let seed = 1; seed <= 150; seed += 1) {
      const r = generator(seed);
      const { holdings: before, accountsIncome } = income(seed, holdings(r));
      const sent = pushes(r, before);
      const rows = before.map(rowOf);
      const live = applyTicks(rows, ticksOf(sent));
      netMoved += live.filter(
        (row, index) => row.income && row.income.netTotalReturnPct !== rows[index]?.income?.netTotalReturnPct,
      ).length;
      withUnallocated += groupsOf(before, 'sector', accountsIncome).groups.filter(
        (g) => g.key === '~unallocated',
      ).length;
      moved += live.filter((row, index) => row !== rows[index]).length;
      counted += live.filter((row) => row.dayChange !== null).length;
      leftOut += live.filter((row) => row.status === 'open' && row.priced && row.dayChange === null).length;
      unpriced += live.filter((row) => row.status === 'open' && !row.priced).length;
      unmapped += live.filter((row) => row.instrumentId === null).length;
    }
    for (const n of [moved, counted, leftOut, unpriced, unmapped, netMoved]) expect(n).toBeGreaterThan(50);
    expect(withUnallocated).toBeGreaterThan(50);
  });
});

describe('where the recompute is cautious (it never invents a day change)', () => {
  const instrument = uuid('ee', 1);
  const base: ModelHolding = {
    id: uuid('11', 1),
    accountId: uuid('aa', 0),
    bucketId: uuid('bb', 0),
    sector: 'IT',
    instrumentId: instrument,
    qty: 10,
    status: 'open',
    lots: [{ qty: 10, unitCostPaise: 100_000, closed: false, closePricePaise: 0 }],
    pricePaise: 120_000,
    priceAt: yesterday,
    source: 'sync',
    previousClosePaise: 118_000,
    previousCloseAt: null,
  };

  it('leaves a live price out of the day change while the close is not known to be of today, as the server does', () => {
    const tick = { instrumentId: instrument, pricePaise: 121_000, at: today + minute, source: 'live' as const };
    const row = applyTicks([rowOf(base)], ticksOf([tick]))[0]!;
    expect(row.dayChange).toBeNull();
    expect(row.returns.value).toBe(12_100);
    expectRow(row, rowOf((writePrice([base], instrument, tick.pricePaise, tick.at, 'live') as [ModelHolding])[0]));
  });

  it('may leave out a holding the server counts when a quote of today wrote the close under a newer live price (healed by the next read)', () => {
    const quoted: ModelHolding = { ...base, source: 'live', priceAt: today + minute, previousCloseAt: today };
    const tick = { instrumentId: instrument, pricePaise: 121_000, at: today + 2 * minute, source: 'live' as const };
    const server = rowOf((writePrice([quoted], instrument, tick.pricePaise, tick.at, 'live') as [ModelHolding])[0]);
    // The read said "live, no day change" only because the close was old; the server now counts it, the browser cannot know.
    const read = rowOf({ ...quoted, previousCloseAt: yesterday });
    expect(read.dayChange).toBeNull();
    expect(server.dayChange).not.toBeNull();
    expect(applyTicks([read], ticksOf([tick]))[0]?.dayChange).toBeNull();
  });

  it('ignores a price that is not newer than the one on the row, and a price of zero', () => {
    const row = rowOf({ ...base, source: 'live', priceAt: today + 5 * minute, previousCloseAt: today });
    const older = ticksOf([{ instrumentId: instrument, pricePaise: 130_000, at: today, source: 'live' }]);
    expect(applyTicks([row], older)[0]).toBe(row);
    const zero = ticksOf([{ instrumentId: instrument, pricePaise: 0, at: today + 9 * minute, source: 'live' }]);
    expect(applyTicks([row], zero)[0]).toBe(row);
  });

  it('refuses totals that are not made of the holdings it was given', () => {
    const summary = buildSummary({ openCount: 5 });
    expect(liveSummary(summary, [rowOf(base)])).toBeUndefined();
  });
});

describe('the recompute is fast enough to run on every push', () => {
  /**
   * The aim is one frame (16 ms) on a normal machine. A wall-clock assertion on a shared CI box or a busy laptop must not
   * flake, so the test fails only above 50 ms - three times the aim, which still catches an accidental quadratic.
   */
  it('works 500 rows, the tiles and a grouping out well inside a frame (median of 25 runs, after a warm-up)', () => {
    const r = generator(7);
    const many = Array.from({ length: 60 }, () => holdings(r)).flat();
    const base = many.slice(0, 500).map((h, n) => ({ ...h, id: uuid('11', 1_000 + n) }));
    const rows = base.map(rowOf);
    const open = rows.filter((row) => row.status === 'open');
    const ticks = ticksOf(pushes(r, base));
    const groups = groupsOf(base, 'account');
    const summary = summaryOf(base);
    const membership = membershipOf('account', undefined)!;
    expect(base).toHaveLength(500);

    const times: number[] = [];
    for (let run = 0; run < 30; run += 1) {
      const started = performance.now();
      const live = applyTicks(rows, ticks);
      const now = live.filter((row) => row.status === 'open');
      liveSummary(summary, now);
      liveGroups(groups, now, membership, false);
      // The first runs warm the JIT up.
      if (run >= 5) times.push(performance.now() - started);
    }
    times.sort((a, b) => a - b);
    const median = times[12] ?? Number.POSITIVE_INFINITY;
    expect(open.length).toBeGreaterThan(100);
    expect(median).toBeLessThan(50);
  });
});
