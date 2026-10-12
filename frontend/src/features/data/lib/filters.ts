/** The state of the coverage table. It lives in the URL, so a view can be shared and Back works. */
export const coverageStatuses = ['current', 'behind', 'empty'] as const;
export type CoverageStatus = (typeof coverageStatuses)[number];

export const sortFields = ['symbol', 'name', 'firstBar', 'lastBar', 'bars'] as const;
export type SortField = (typeof sortFields)[number];

export const pageSizes = [25, 50, 100] as const;
export const defaultPageSize = 25;

export interface SortState {
  field: SortField;
  descending: boolean;
}

export interface CoverageFilters {
  q: string;
  status: CoverageStatus | null;
  sort: SortState;
  page: number;
  pageSize: number;
}

export const defaultSort: SortState = { field: 'symbol', descending: false };

export const emptyFilters: CoverageFilters = {
  q: '',
  status: null,
  sort: defaultSort,
  page: 1,
  pageSize: defaultPageSize,
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

function positiveInt(value: string | null, fallback: number): number {
  const parsed = Number(value);
  return Number.isInteger(parsed) && parsed >= 1 ? parsed : fallback;
}

/** Reads the table state from URL parameters; anything unknown or malformed falls back to the default. */
export function parseFilters(params: URLSearchParams): CoverageFilters {
  const status = params.get('status');
  const size = positiveInt(params.get('size'), defaultPageSize);
  return {
    q: params.get('q') ?? '',
    status: isStatus(status) ? status : null,
    sort: parseSort(params.get('sort')),
    page: positiveInt(params.get('page'), 1),
    pageSize: pageSizes.some((s) => s === size) ? size : defaultPageSize,
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
  set('sort', formatSort(filters.sort) === formatSort(defaultSort) ? null : formatSort(filters.sort));
  set('page', filters.page > 1 ? String(filters.page) : null);
  set('size', filters.pageSize !== defaultPageSize ? String(filters.pageSize) : null);
  return next;
}

/** Number of narrowing filters in use (search text, status). */
export const activeFilterCount = (f: CoverageFilters): number => [f.q.trim() !== '', f.status].filter(Boolean).length;
