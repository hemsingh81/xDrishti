/** The state of the coverage list. It lives in the URL, so a view can be shared and Back works. */
export const coverageStatuses = ['current', 'behind', 'empty'] as const;
export type CoverageStatus = (typeof coverageStatuses)[number];

export const sortFields = ['symbol', 'name', 'firstBar', 'lastBar', 'bars'] as const;
export type SortField = (typeof sortFields)[number];

export interface SortState {
  field: SortField;
  descending: boolean;
}

export interface CoverageFilters {
  q: string;
  status: CoverageStatus | null;
  /** Only instruments with open gaps. */
  hasGaps: boolean;
  sort: SortState;
}

export const defaultSort: SortState = { field: 'symbol', descending: false };

export const emptyFilters: CoverageFilters = {
  q: '',
  status: null,
  hasGaps: false,
  sort: defaultSort,
};

const isStatus = (value: string | null): value is CoverageStatus => coverageStatuses.some((s) => s === value);
/** True when `value` names a column the API can sort by. */
export const isSortField = (value: string): value is SortField => sortFields.some((f) => f === value);

export function parseSort(value: string | null): SortState {
  const descending = value?.startsWith('-') ?? false;
  const field = descending ? (value?.slice(1) ?? '') : (value ?? '');
  return isSortField(field) ? { field, descending } : defaultSort;
}

export const formatSort = (sort: SortState): string => `${sort.descending ? '-' : ''}${sort.field}`;

/** Reads the table state from URL parameters; anything unknown or malformed falls back to the default. */
export function parseFilters(params: URLSearchParams): CoverageFilters {
  const status = params.get('status');
  return {
    q: params.get('q') ?? '',
    status: isStatus(status) ? status : null,
    hasGaps: params.get('hasGaps') === 'true',
    sort: parseSort(params.get('sort')),
  };
}

/** Only non-default values are written, so the URL of the plain page stays `/data`. */
export function toParams(filters: CoverageFilters, extra: URLSearchParams = new URLSearchParams()): URLSearchParams {
  const next = new URLSearchParams(extra);
  const set = (key: string, value: string | null) => {
    if (value) next.set(key, value);
    else next.delete(key);
  };
  set('q', filters.q.trim() || null);
  set('status', filters.status);
  set('hasGaps', filters.hasGaps ? 'true' : null);
  set('sort', formatSort(filters.sort) === formatSort(defaultSort) ? null : formatSort(filters.sort));
  // The list loads further rows as it is scrolled: the page and size of older links are dropped.
  set('page', null);
  set('size', null);
  return next;
}

/** Number of narrowing filters in use (search text, status, has gaps). */
export const activeFilterCount = (f: CoverageFilters): number =>
  [f.q.trim() !== '', f.status, f.hasGaps].filter(Boolean).length;
