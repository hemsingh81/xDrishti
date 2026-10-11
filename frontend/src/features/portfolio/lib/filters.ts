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
  'acquiredOn',
  'bucket',
  'account',
] as const;
export type SortField = (typeof sortFields)[number];

export const pageSizes = [25, 50, 100] as const;
export const defaultPageSize = 50;

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
  page: number;
  pageSize: number;
}

export const emptyFilters: PortfolioFilters = {
  account: null,
  bucket: null,
  status: 'open',
  q: '',
  sort: defaultSort,
  page: 1,
  pageSize: defaultPageSize,
};

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

function positiveInt(value: string | null, fallback: number): number {
  const parsed = Number(value);
  return Number.isInteger(parsed) && parsed >= 1 ? parsed : fallback;
}

/** Reads the page state from URL parameters; anything unknown or malformed falls back to the default. */
export function parseFilters(params: URLSearchParams): PortfolioFilters {
  const status = params.get('status');
  const size = positiveInt(params.get('size'), defaultPageSize);
  return {
    account: nonEmpty(params.get('account')),
    bucket: nonEmpty(params.get('bucket')),
    status: isStatus(status) ? status : 'open',
    q: params.get('q') ?? '',
    sort: parseSort(params.get('sort')),
    page: positiveInt(params.get('page'), 1),
    pageSize: pageSizes.some((s) => s === size) ? size : defaultPageSize,
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
  set('page', filters.page > 1 ? String(filters.page) : null);
  set('size', filters.pageSize !== defaultPageSize ? String(filters.pageSize) : null);
  return next;
}

/** Narrowing filters in use besides the account (bucket, search text, status other than open). */
export function activeFilterCount(f: PortfolioFilters): number {
  return [f.bucket, f.q.trim() !== '', f.status !== 'open'].filter(Boolean).length;
}
