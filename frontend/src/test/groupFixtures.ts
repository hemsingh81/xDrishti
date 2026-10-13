import { http, HttpResponse } from 'msw';
import type { GroupPart, HoldingGroup, HoldingGroupTotal, HoldingGroups, HoldingPage } from '@/shared/api';
import { buildHolding, holdingIds, ids, sampleHoldings, searchHoldings } from './fixtures';

/** Group keys as the API sends them: the account id, bucket id, sector name, basket id; `~none` for Unclassified / No basket. */
export const groupKeys = {
  mainAccount: ids.main,
  swingAccount: ids.swing,
  longTerm: '00000000-0000-4000-8000-0000000000b2',
  trading: '00000000-0000-4000-8000-0000000000b3',
  energy: 'Energy',
  it: 'Information Technology',
  oilGas: 'Oil & Gas',
  nifty: '00000000-0000-4000-8000-0000000000c1',
  itBasket: '00000000-0000-4000-8000-0000000000c2',
  none: '~none',
} as const;

/** Figures of a group that is fully priced: a gain of 16.1 % on ₹1,00,000, XIRR 14.2 %, day change +₹900. */
export function buildGroup(overrides: Partial<HoldingGroup> = {}): HoldingGroup {
  return {
    key: groupKeys.energy,
    name: 'Energy',
    holdings: 1,
    unpriced: 0,
    invested: 100_000,
    value: 116_100,
    pnl: 16_100,
    pnlPct: 16.1,
    realised: 0,
    xirr: 14.2,
    xirrState: 'ok',
    dayChange: 900,
    dayChangePct: 0.78,
    dayChangeExcluded: 0,
    weightPct: 57.08,
    parts: null,
    ...overrides,
  };
}

/** A group whose holdings have no price: no value, no P&L, no XIRR, no weight. */
export const buildUnpricedGroup = (overrides: Partial<HoldingGroup> = {}): HoldingGroup =>
  buildGroup({
    key: groupKeys.oilGas,
    name: 'Oil & Gas',
    holdings: 2,
    unpriced: 2,
    // As the real API: holdings without a price are left out, invested included.
    invested: 0,
    value: null,
    pnl: null,
    pnlPct: null,
    xirr: null,
    xirrState: 'no_price',
    dayChange: null,
    dayChangePct: null,
    dayChangeExcluded: 2,
    weightPct: null,
    ...overrides,
  });

/** The scope total of the sample holdings: ₹1,85,000 invested, ₹2,03,400 now, as in the summary. */
export const buildGroupTotal = (overrides: Partial<HoldingGroupTotal> = {}): HoldingGroupTotal => ({
  holdings: 4,
  unpriced: 0,
  invested: 185_000,
  value: 203_400,
  pnl: 18_400,
  pnlPct: 9.95,
  realised: 800,
  xirr: 11.3,
  xirrState: 'ok',
  dayChange: 900,
  dayChangePct: 0.56,
  dayChangeExcluded: 1,
  weightPct: 100,
  ...overrides,
});

export const buildGroups = (overrides: Partial<HoldingGroups> = {}): HoldingGroups => ({
  by: 'sector',
  additive: true,
  stackBy: null,
  groups: [],
  total: buildGroupTotal(),
  ...overrides,
});

const part = (key: string, name: string, value: number): GroupPart => ({ key, name, value });
const main = (value: number) => part(groupKeys.mainAccount, 'Test Dhan Main', value);
const swing = (value: number) => part(groupKeys.swingAccount, 'Test Dhan Swing', value);

/** The sample holdings as sectors (largest value first): Energy 116,100 · Information Technology 81,300 · Unclassified 6,000. */
const sectorGroups = (stack: boolean): HoldingGroup[] => [
  buildGroup({ parts: stack ? [main(116_100)] : null }),
  buildGroup({
    key: groupKeys.it,
    name: 'Information Technology',
    holdings: 2,
    invested: 80_000,
    value: 81_300,
    pnl: 1_300,
    pnlPct: 1.63,
    xirr: null,
    xirrState: 'insufficient',
    dayChange: -200,
    dayChangePct: -0.51,
    dayChangeExcluded: 1,
    weightPct: 39.97,
    parts: stack ? [main(38_800), swing(42_500)] : null,
  }),
  buildGroup({
    key: groupKeys.none,
    name: 'Unclassified',
    invested: 5_000,
    value: 6_000,
    pnl: 1_000,
    pnlPct: 20,
    xirr: 1_500,
    xirrState: 'n_m',
    dayChange: 200,
    dayChangePct: 3.45,
    weightPct: 2.95,
    parts: stack ? [swing(6_000)] : null,
  }),
];

/** The sample holdings by account: Main 154,900 (RELIANCE, TCS) and Swing 48,500 (INFY, SMALLCO). */
const accountGroups = (stack: boolean): HoldingGroup[] => {
  return [
    buildGroup({
      key: groupKeys.mainAccount,
      name: 'Test Dhan Main',
      holdings: 2,
      invested: 140_000,
      value: 154_900,
      pnl: 14_900,
      pnlPct: 10.64,
      xirr: 9.8,
      dayChange: 700,
      dayChangePct: 0.45,
      weightPct: 76.15,
      parts: stack
        ? [part('Energy', 'Energy', 116_100), part('Information Technology', 'Information Technology', 38_800)]
        : null,
    }),
    buildGroup({
      key: groupKeys.swingAccount,
      name: 'Test Dhan Swing',
      holdings: 2,
      invested: 45_000,
      value: 48_500,
      pnl: 3_500,
      pnlPct: 7.78,
      xirr: null,
      xirrState: 'insufficient',
      dayChange: 200,
      dayChangePct: 0.41,
      dayChangeExcluded: 1,
      weightPct: 23.85,
      parts: stack
        ? [part('Information Technology', 'Information Technology', 42_500), part('~none', 'Unclassified', 6_000)]
        : null,
    }),
  ];
};

const bucketGroups = (): HoldingGroup[] => [
  buildGroup({
    key: groupKeys.longTerm,
    name: 'Long term',
    holdings: 2,
    invested: 140_000,
    value: 154_900,
    weightPct: 76.15,
  }),
  buildGroup({
    key: groupKeys.trading,
    name: 'Trading',
    holdings: 2,
    invested: 45_000,
    value: 48_500,
    weightPct: 23.85,
  }),
];

/** Baskets overlap: TCS and INFY are in two baskets, so the weights add up to more than 100 %. */
const basketGroups = (): HoldingGroup[] => [
  buildGroup({
    key: groupKeys.nifty,
    name: 'Nifty 50 core',
    holdings: 3,
    invested: 180_000,
    value: 197_400,
    weightPct: 97.05,
  }),
  buildGroup({
    key: groupKeys.itBasket,
    name: 'IT leaders',
    holdings: 2,
    invested: 80_000,
    value: 81_300,
    weightPct: 39.97,
  }),
  buildGroup({ key: groupKeys.none, name: 'No basket', holdings: 1, invested: 5_000, value: 6_000, weightPct: 2.95 }),
];

/** Which sample holdings belong to which group (by grouping and key): what the list of an opened group answers. */
export const groupMembers: Record<string, Record<string, readonly string[]>> = {
  account: {
    [groupKeys.mainAccount]: [holdingIds.reliance, holdingIds.tcs],
    [groupKeys.swingAccount]: [holdingIds.infy, holdingIds.smallco],
  },
  bucket: {
    [groupKeys.longTerm]: [holdingIds.reliance, holdingIds.tcs],
    [groupKeys.trading]: [holdingIds.infy, holdingIds.smallco],
  },
  sector: {
    [groupKeys.energy]: [holdingIds.reliance],
    [groupKeys.it]: [holdingIds.tcs, holdingIds.infy],
    [groupKeys.none]: [holdingIds.smallco],
  },
  basket: {
    [groupKeys.nifty]: [holdingIds.reliance, holdingIds.tcs, holdingIds.infy],
    [groupKeys.itBasket]: [holdingIds.tcs, holdingIds.infy],
    [groupKeys.none]: [holdingIds.smallco],
  },
};

/** The groups of the sample holdings for a grouping; `stack` adds the parts of the stacked bar (account or sector only). */
export function sampleGroups(by: string, stackBy: string | null = null): HoldingGroups {
  const stack = stackBy !== null;
  const groups =
    by === 'account'
      ? accountGroups(stack)
      : by === 'bucket'
        ? bucketGroups()
        : by === 'basket'
          ? basketGroups()
          : sectorGroups(stack);
  return buildGroups({ by, additive: by !== 'basket', stackBy, groups });
}

const sortNumber = (g: HoldingGroup, field: string): number | string | null => {
  const values: Record<string, number | string | null> = {
    value: g.value,
    invested: g.invested,
    pnl: g.pnl,
    pnlPct: g.pnlPct,
    xirr: g.xirr,
    dayChange: g.dayChange,
    name: g.name,
  };
  return values[field] ?? null;
};

/** What GET /api/holdings/groups answers over the sample data: the grouping, the order (nulls last) and the stack parts. */
export function answerGroups(params: URLSearchParams): HoldingGroups {
  const data = sampleGroups(params.get('by') ?? 'sector', params.get('stackBy'));
  const sort = params.get('sort') ?? '-value';
  const field = sort.replace(/^-/, '');
  const direction = sort.startsWith('-') ? -1 : 1;
  const groups = [...data.groups].sort((a, b) => {
    const x = sortNumber(a, field);
    const y = sortNumber(b, field);
    if (x === null || y === null) return x === y ? 0 : x === null ? 1 : -1;
    return (x < y ? -1 : x > y ? 1 : 0) * direction;
  });
  return { ...data, groups };
}

/** The list of an opened group: the sample holdings that belong to it (an unknown key gives an empty page). */
export function answerGroupHoldings(params: URLSearchParams): HoldingPage {
  const members = groupMembers[params.get('groupBy') ?? '']?.[params.get('groupKey') ?? ''] ?? [];
  return searchHoldings(
    sampleHoldings.filter((h) => members.includes(h.id)),
    params,
  );
}

/** `count` open holdings of one group (S001, S002, …), for a group that needs more than one page. */
export const manyHoldings = (count: number, offset = 0) =>
  Array.from({ length: count }, (_, i) =>
    buildHolding({
      id: `00000000-0000-4000-8000-00000000${String(9000 + offset + i)}`,
      symbol: `S${String(offset + i + 1).padStart(3, '0')}`,
      name: `Stock ${String(offset + i + 1)}`,
    }),
  );

/** Default API mocks of the groups (P2-08): the sample holdings grouped, in the order asked for. */
export const groupHandlers = [
  http.get('/api/holdings/groups', ({ request }) => HttpResponse.json(answerGroups(new URL(request.url).searchParams))),
];
