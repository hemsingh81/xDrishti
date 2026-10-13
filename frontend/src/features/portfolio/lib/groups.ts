import type { GroupBy, Grouping, SortState } from './filters';

export const groupingLabels: Record<Grouping, string> = {
  none: 'None',
  account: 'Account',
  bucket: 'Bucket',
  sector: 'Sector',
  basket: 'Basket',
};

/** The reserved key of "Unclassified" (sector) and "No basket": it cannot be a real sector name or id. */
export const noGroupKey = '~none';

/** The sort fields of the groups endpoint (a subset of the holdings' fields, with the same names). */
export const groupSortFields = ['value', 'invested', 'pnl', 'pnlPct', 'xirr', 'dayChange', 'name'] as const;
export type GroupSortField = (typeof groupSortFields)[number];

/** Largest value first, as the endpoint's default: the screen sends it explicitly. */
export const defaultGroupSort: SortState = { field: 'value', descending: true };

export const isGroupSortField = (field: string): field is GroupSortField => groupSortFields.some((f) => f === field);

/**
 * The order of the groups: the page's sort when it is one the groups have (the sort of the flat list is shared, so a
 * link with `sort=-pnl` orders the groups by P&L), else largest value first.
 */
export function groupSortOf(sort: SortState): SortState {
  return isGroupSortField(sort.field) ? sort : defaultGroupSort;
}

/** The second dimension the stacked bar needs: sectors of each account, accounts of each sector; none for the others. */
export function stackByOf(by: GroupBy): 'account' | 'sector' | undefined {
  if (by === 'sector') return 'account';
  return by === 'account' ? 'sector' : undefined;
}

/** What a group is called in the singular, for headings and labels. */
export const groupNoun: Record<GroupBy, string> = {
  account: 'Account',
  bucket: 'Bucket',
  sector: 'Sector',
  basket: 'Basket',
};

/** "3 holdings without a price" / "1 without a price": the flag of a group with holdings Dhan reported without a usable price. */
export function unpricedText(holdings: number, unpriced: number): string | null {
  if (unpriced <= 0) return null;
  if (unpriced === holdings) return `${String(unpriced)} ${unpriced === 1 ? 'holding' : 'holdings'} without a price`;
  return `${String(unpriced)} without a price`;
}

/** The order options of the small sort control (phone): the field and the direction in words. */
export const groupSortOptions: readonly { field: GroupSortField; descending: boolean; label: string }[] = [
  { field: 'value', descending: true, label: 'Value, high to low' },
  { field: 'value', descending: false, label: 'Value, low to high' },
  { field: 'invested', descending: true, label: 'Invested, high to low' },
  { field: 'invested', descending: false, label: 'Invested, low to high' },
  { field: 'pnl', descending: true, label: 'P&L, best first' },
  { field: 'pnl', descending: false, label: 'P&L, worst first' },
  { field: 'pnlPct', descending: true, label: 'P&L %, best first' },
  { field: 'pnlPct', descending: false, label: 'P&L %, worst first' },
  { field: 'xirr', descending: true, label: 'XIRR, best first' },
  { field: 'xirr', descending: false, label: 'XIRR, worst first' },
  { field: 'dayChange', descending: true, label: 'Day change, best first' },
  { field: 'dayChange', descending: false, label: 'Day change, worst first' },
  { field: 'name', descending: false, label: 'Name, A to Z' },
  { field: 'name', descending: true, label: 'Name, Z to A' },
];

/** Sorting by the sorted field flips the direction; a new field starts ascending (as in the holdings grid). */
export function nextGroupSort(current: SortState, field: GroupSortField): SortState {
  return current.field === field ? { field, descending: !current.descending } : { field, descending: false };
}
