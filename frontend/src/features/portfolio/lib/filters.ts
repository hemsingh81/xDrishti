/** The view state of the Portfolio page. It lives in the URL, so a view can be shared and Back works. */
export const holdingStatuses = ['open', 'closed', 'all'] as const;
export type HoldingStatus = (typeof holdingStatuses)[number];

/** The sort fields the API accepts; the ids of the sortable grid columns. */
export const sortFields = [
  'symbol',
  'name',
  'qty',
  'avgCost',
  'lastPrice',
  'invested',
  'value',
  'pnl',
  'pnlPct',
  'xirr',
  'dividends',
  'dayChange',
  'dayChangePct',
  'acquiredOn',
  'bucket',
  'account',
] as const;
export type SortField = (typeof sortFields)[number];

/** Group by: `none` is the flat holdings list (the default); the others group the holdings, one row per group (P2-08). */
export const groupings = ['none', 'account', 'bucket', 'sector', 'basket'] as const;
export type Grouping = (typeof groupings)[number];
export type GroupBy = Exclude<Grouping, 'none'>;

export interface SortState {
  field: SortField;
  descending: boolean;
}

export const defaultSort: SortState = { field: 'symbol', descending: false };

export interface PortfolioFilters {
  /** Account id; null = all accounts. */
  account: string | null;
  /** Bucket id; null = all buckets. */
  bucket: string | null;
  status: HoldingStatus;
  q: string;
  sort: SortState;
  groupBy: Grouping;
}

export const emptyFilters: PortfolioFilters = {
  account: null,
  bucket: null,
  status: 'open',
  q: '',
  sort: defaultSort,
  groupBy: 'none',
};

const isGrouping = (value: string | null): value is Grouping => groupings.some((g) => g === value);
const isStatus = (value: string | null): value is HoldingStatus => holdingStatuses.some((s) => s === value);
/** True when `value` names a column the API can sort by. */
export const isSortField = (value: string): value is SortField => sortFields.some((f) => f === value);

export function parseSort(value: string | null): SortState {
  const descending = value?.startsWith('-') ?? false;
  const field = descending ? (value?.slice(1) ?? '') : (value ?? '');
  return isSortField(field) ? { field, descending } : defaultSort;
}

export const formatSort = (sort: SortState): string => `${sort.descending ? '-' : ''}${sort.field}`;

const nonEmpty = (value: string | null): string | null => (value === null || value === '' ? null : value);

/** Reads the page state from URL parameters; anything unknown or malformed falls back to the default. */
export function parseFilters(params: URLSearchParams): PortfolioFilters {
  const status = params.get('status');
  const groupBy = params.get('groupBy');
  return {
    account: nonEmpty(params.get('account')),
    bucket: nonEmpty(params.get('bucket')),
    status: isStatus(status) ? status : 'open',
    q: params.get('q') ?? '',
    sort: parseSort(params.get('sort')),
    groupBy: isGrouping(groupBy) ? groupBy : 'none',
  };
}

/** Only non-default values are written, so the plain list stays `/portfolio`. Other parameters (the open holding) are kept. */
export function toParams(filters: PortfolioFilters, extra: URLSearchParams = new URLSearchParams()): URLSearchParams {
  const next = new URLSearchParams(extra);
  const set = (key: string, value: string | null) => {
    if (value) next.set(key, value);
    else next.delete(key);
  };
  set('account', filters.account);
  set('bucket', filters.bucket);
  set('status', filters.status === 'open' ? null : filters.status);
  set('q', filters.q.trim() || null);
  set('sort', formatSort(filters.sort) === formatSort(defaultSort) ? null : formatSort(filters.sort));
  set('groupBy', filters.groupBy === 'none' ? null : filters.groupBy);
  // The list loads as the owner scrolls: an old link's page and size are dropped.
  set('page', null);
  set('size', null);
  return next;
}

/** Narrowing filters in use besides the account (bucket, search text, status other than open). */
export function activeFilterCount(f: PortfolioFilters): number {
  return [f.bucket, f.q.trim() !== '', f.status !== 'open'].filter(Boolean).length;
}
