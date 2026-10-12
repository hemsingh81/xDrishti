/** The state of the instruments table (search, status, sort). It lives in the URL, so a view can be shared and Back works. */
export const qualityStatuses = ['pass', 'warning', 'fail', 'notChecked'] as const;
export type QualityStatus = (typeof qualityStatuses)[number];

export const sortFields = [
  'symbol',
  'name',
  'status',
  'score',
  'coverage',
  'barsChecked',
  'missingMinutes',
  'checkedAt',
] as const;
export type SortField = (typeof sortFields)[number];

export interface SortState {
  field: SortField;
  descending: boolean;
}

export interface QualityFilters {
  q: string;
  status: QualityStatus | null;
  sort: SortState;
}

/** Worst first: the API orders status by severity (fail highest). */
export const defaultSort: SortState = { field: 'status', descending: true };

export const emptyFilters: QualityFilters = {
  q: '',
  status: null,
  sort: defaultSort,
};

export const isStatus = (value: string | null): value is QualityStatus => qualityStatuses.some((s) => s === value);
export const isSortField = (value: string): value is SortField => sortFields.some((f) => f === value);

export const formatSort = (sort: SortState): string => `${sort.descending ? '-' : ''}${sort.field}`;

export function parseSort(value: string | null): SortState {
  const descending = value?.startsWith('-') ?? false;
  const field = descending ? (value?.slice(1) ?? '') : (value ?? '');
  return isSortField(field) ? { field, descending } : defaultSort;
}

/** Reads the table state from URL parameters; anything unknown or malformed falls back to the default. */
export function parseFilters(params: URLSearchParams): QualityFilters {
  const status = params.get('status');
  return {
    q: params.get('q') ?? '',
    status: isStatus(status) ? status : null,
    sort: parseSort(params.get('sort')),
  };
}

/** Only non-default values are written, so the URL of the plain page stays `/quality`. Other parameters are kept. */
export function toParams(filters: QualityFilters, extra: URLSearchParams = new URLSearchParams()): URLSearchParams {
  const next = new URLSearchParams(extra);
  const set = (key: string, value: string | null) => {
    if (value) next.set(key, value);
    else next.delete(key);
  };
  set('q', filters.q.trim() || null);
  set('status', filters.status);
  set('sort', formatSort(filters.sort) === formatSort(defaultSort) ? null : formatSort(filters.sort));
  // The list loads as the owner scrolls: an old link's page and size are dropped.
  set('page', null);
  set('size', null);
  return next;
}

/** Number of narrowing filters in use (search text, status). */
export const activeFilterCount = (f: QualityFilters): number => [f.q.trim() !== '', f.status].filter(Boolean).length;
