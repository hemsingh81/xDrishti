import type { HoldingGroup, HoldingGroups, HoldingGroupTotal, HoldingRow, HoldingsSummary } from '@/shared/api';
import { figuresOf, incomeAt, weightOf, type LiveFigures } from './liveFigures';
import type { GroupBy } from './filters';
import { isUnallocatedKey } from './groups';

/**
 * The tiles at the pushed prices, from every open holding of the scope (already at those prices). The counts, the XIRR, the
 * benchmark, the realised result and the net XIRR are the last read's; the dividends and costs are too (they do not move with
 * a price), the two total return percentages are worked out again. Undefined when the holdings are not those the totals were made of
 * (the two reads were of different moments, or the holdings changed): the totals then stay as read, until the next read.
 */
export function liveSummary(summary: HoldingsSummary, rows: readonly HoldingRow[]): HoldingsSummary | undefined {
  const f = figuresOf(rows);
  if (f.open !== summary.openCount) return undefined;
  return {
    ...summary,
    returns: { ...summary.returns, invested: f.invested, value: f.value, pnl: f.pnl, pnlPct: f.pnlPct },
    income: incomeAt(summary.income, summary.returns.realisedPnl, f.invested, f.pnl),
    unpricedCount: f.open - f.priced,
    dayChange: f.dayChange,
    dayChangePct: f.dayChangePct,
    dayChangeExcluded: f.dayChangeExcluded,
    pricesAsOf: { oldest: f.oldestPriceAt, newest: f.newestPriceAt },
  };
}

/** Who is in a group: accounts and buckets by the row's own ids; sectors and baskets by the lists the server gave per group key. */
export type GroupMembership =
  | { by: Extract<GroupBy, 'account' | 'bucket'> }
  | { by: Extract<GroupBy, 'sector' | 'basket'>; members: ReadonlyMap<string, ReadonlySet<string>> };

/** The membership of a grouping: nothing to read for accounts and buckets; for the others the lists per group key (undefined until read). */
export function membershipOf(
  by: GroupBy,
  lists: readonly (readonly [string, readonly string[]])[] | undefined,
): GroupMembership | undefined {
  if (by === 'account' || by === 'bucket') return { by };
  return lists === undefined ? undefined : { by, members: new Map(lists.map(([key, ids]) => [key, new Set(ids)])) };
}

function membersOf(group: HoldingGroup, rows: readonly HoldingRow[], membership: GroupMembership) {
  if (!('members' in membership)) {
    return rows.filter((row) => (membership.by === 'account' ? row.accountId : row.bucketId) === group.key);
  }
  const ids = membership.members.get(group.key);
  return ids === undefined ? undefined : rows.filter((row) => ids.has(row.id));
}

type Figures = Pick<
  HoldingGroupTotal,
  'unpriced' | 'invested' | 'value' | 'pnl' | 'pnlPct' | 'dayChange' | 'dayChangePct' | 'dayChangeExcluded'
>;

/** A group's figures; a group of only unpriced holdings has no value (it is not worth zero), as on the server. */
function figuresFor(f: LiveFigures, holdings: number): Figures | undefined {
  const closed = holdings - f.open;
  if (closed < 0) return undefined;
  const nothingPriced = f.open > 0 && f.priced === 0 && closed === 0;
  return {
    unpriced: f.open - f.priced,
    invested: f.invested,
    value: nothingPriced ? null : f.value,
    pnl: nothingPriced ? null : f.pnl,
    pnlPct: nothingPriced ? null : f.pnlPct,
    dayChange: f.dayChange,
    dayChangePct: f.dayChangePct,
    dayChangeExcluded: f.dayChangeExcluded,
  };
}

/**
 * The groups and their total row at the pushed prices, from every open holding of the scope (already at those prices).
 * `holdings` and `realised`, the XIRR and the parts of the stacked bar are the last read's, and so is the order (rows do not
 * jump while the owner reads). The Unallocated row (dividends and costs of no group, no holdings) is left as read: it has no
 * price to move. Undefined when the holdings do not fit the groups (counts differ, a group's members are not known): the
 * groups then stay as read.
 */
export function liveGroups(
  data: HoldingGroups,
  rows: readonly HoldingRow[],
  membership: GroupMembership,
  statusOpen: boolean,
): HoldingGroups | undefined {
  const scope = figuresOf(rows);
  const total = figuresFor(scope, data.total.holdings);
  if (!total || (statusOpen && scope.open !== data.total.holdings)) return undefined;

  const scopeValue = total.value;
  const groups: HoldingGroup[] = [];
  for (const group of data.groups) {
    if (isUnallocatedKey(group.key)) {
      groups.push(group);
      continue;
    }
    const members = membersOf(group, rows, membership);
    if (!members) return undefined;
    const figures = figuresFor(figuresOf(members), group.holdings);
    if (!figures || (statusOpen && members.length !== group.holdings)) return undefined;
    groups.push({
      ...group,
      ...figures,
      weightPct: weightOf(figures.value, scopeValue),
      income: incomeAt(group.income, group.realised, figures.invested, figures.pnl),
    });
  }
  return {
    ...data,
    groups,
    total: {
      ...data.total,
      ...total,
      weightPct: weightOf(total.value, total.value),
      income: incomeAt(data.total.income, data.total.realised, total.invested, total.pnl),
    },
  };
}
