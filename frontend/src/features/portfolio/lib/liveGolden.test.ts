import type { HoldingGroup, HoldingGroups, HoldingRow, HoldingsSummary } from '@/shared/api';
import golden from '@/test/fixtures/liveGolden.json';
import { applyTicks } from './liveFigures';
import { applyPrices, noTicks, type LiveTickState } from './liveTicks';
import { liveGroups, liveSummary, membershipOf } from './liveTotals';
import type { GroupBy } from './filters';

/**
 * Acceptance criterion 5, against the server itself: `liveGolden.json` is written by the backend test `LiveGoldenTests` from
 * the REAL C# calculators (`ReturnsCalculator`, `DayChangeCalculator`, `GroupCalculator`, the summary). Each scenario has a
 * state read from the API (`before`), the pushes that follow and the state the API gives for the same prices (`after`). The
 * browser applies the pushes to `before` and must end up with `after` exactly: money and percentages are four decimals, halves
 * away from zero, on both sides, so there is no tolerance. What a push cannot change (XIRR, benchmark: null/"unavailable" in
 * the fixture) is not compared.
 */

interface GroupSet {
  account: HoldingGroups;
  bucket: HoldingGroups;
  sector: HoldingGroups;
}
interface State {
  rows: HoldingRow[];
  summary: HoldingsSummary;
  groups: { all: GroupSet; open: GroupSet };
  sectorMembers: Record<string, string[]>;
}
interface Scenario {
  name: string;
  description: string;
  pushes: { instrumentId: string; lastPrice: number; priceAt: string; source: string }[];
  before: State;
  after: State;
}
const file = golden as unknown as { schema: number; scenarios: Scenario[] };

/** The pushes in order, one message each (the hub sends one message per second; the order is what matters). */
function ticksOf(pushes: Scenario['pushes']): LiveTickState {
  return pushes.reduce<LiveTickState>(
    (state, push, index) => applyPrices(state, { asOf: push.priceAt, instruments: [push] }, index),
    noTicks,
  );
}

/** The figures a push can change; the rest of a row is the read's. */
const rowFigures = (row: HoldingRow) => ({
  id: row.id,
  lastPrice: row.lastPrice,
  lastPriceAt: row.lastPriceAt,
  priceAt: row.priceAt,
  priceSource: row.priceSource,
  priced: row.priced,
  value: row.returns.value,
  pnl: row.returns.pnl,
  pnlPct: row.returns.pnlPct,
  dayChange: row.dayChange,
  dayChangePct: row.dayChangePct,
});

const summaryFigures = (summary: HoldingsSummary) => ({
  invested: summary.returns.invested,
  value: summary.returns.value,
  pnl: summary.returns.pnl,
  pnlPct: summary.returns.pnlPct,
  openCount: summary.openCount,
  unpricedCount: summary.unpricedCount,
  dayChange: summary.dayChange,
  dayChangePct: summary.dayChangePct,
  dayChangeExcluded: summary.dayChangeExcluded,
  pricesAsOf: summary.pricesAsOf,
});

function groupFigures(group: HoldingGroup | HoldingGroups['total']) {
  return {
    holdings: group.holdings,
    unpriced: group.unpriced,
    invested: group.invested,
    value: group.value,
    pnl: group.pnl,
    pnlPct: group.pnlPct,
    dayChange: group.dayChange,
    dayChangePct: group.dayChangePct,
    dayChangeExcluded: group.dayChangeExcluded,
    weightPct: group.weightPct,
  };
}

/** The groups by key (the server re-sorts after a push; the browser keeps the order it was read in), and the total row. */
const groupsByKey = (groups: HoldingGroups) => ({
  groups: Object.fromEntries(groups.groups.map((group) => [group.key, groupFigures(group)])),
  keys: groups.groups.map((group) => group.key).sort(),
  total: groupFigures(groups.total),
});

const dimensions: GroupBy[] = ['account', 'bucket', 'sector'];
const open = (rows: readonly HoldingRow[]) => rows.filter((row) => row.status === 'open');

describe('the browser recompute equals the server (golden file written by the real C# calculators)', () => {
  it('has the schema this test reads', () => {
    expect(file.schema).toBe(1);
    expect(file.scenarios.length).toBeGreaterThanOrEqual(8);
  });

  describe.each(file.scenarios.map((scenario) => [scenario.name, scenario] as const))('%s', (_name, scenario) => {
    const { before, after } = scenario;
    const { ticks } = ticksOf(scenario.pushes);
    const rows = applyTicks(before.rows, ticks);

    it('the pushes change something (the comparison below is not between two equal states)', () => {
      expect(after.rows.map(rowFigures)).not.toEqual(before.rows.map(rowFigures));
    });

    it('rows: price, source, value, P&L and day change of every holding, open and closed', () => {
      expect(rows.map(rowFigures)).toEqual(after.rows.map(rowFigures));
    });

    it('tiles: invested, value, P&L, day change, excluded, unpriced and prices-as-of', () => {
      const live = liveSummary(before.summary, open(rows));

      expect(live).toBeDefined();
      expect(summaryFigures(live!)).toEqual(summaryFigures(after.summary));
    });

    it.each(dimensions)('group rows and the total row by %s (open holdings)', (by) => {
      const lists = by === 'sector' ? Object.entries(before.sectorMembers) : undefined;
      const live = liveGroups(before.groups.open[by as keyof GroupSet], open(rows), membershipOf(by, lists)!, true);

      expect(live).toBeDefined();
      const expected = groupsByKey(after.groups.open[by as keyof GroupSet]);
      const actual = groupsByKey(live!);
      expect(actual.keys).toEqual(expected.keys);
      expect(actual.groups).toEqual(expected.groups);
      expect(actual.total).toEqual(expected.total);
    });

    it.each(dimensions)('group rows and the total row by %s (all holdings, closed ones counted)', (by) => {
      const lists = by === 'sector' ? Object.entries(before.sectorMembers) : undefined;
      const live = liveGroups(before.groups.all[by as keyof GroupSet], rows, membershipOf(by, lists)!, false);

      expect(live).toBeDefined();
      const expected = groupsByKey(after.groups.all[by as keyof GroupSet]);
      const actual = groupsByKey(live!);
      expect(actual.keys).toEqual(expected.keys);
      expect(actual.groups).toEqual(expected.groups);
      expect(actual.total).toEqual(expected.total);
    });
  });
});
