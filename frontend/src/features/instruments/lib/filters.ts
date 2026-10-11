/** The search state of the Instruments page. It lives in the URL, so a view can be shared and Back works. */
export const instrumentTypes = ['share', 'etf', 'index', 'future'] as const;
export type InstrumentType = (typeof instrumentTypes)[number];

export const sortFields = ['symbol', 'name', 'type', 'series', 'sector', 'lot', 'tick'] as const;
export type SortField = (typeof sortFields)[number];

export const pageSizes = [50, 100] as const;

export interface SortState {
  field: SortField;
  descending: boolean;
}

export interface InstrumentFilters {
  q: string;
  type: InstrumentType | null;
  sector: string | null;
  series: string | null;
  fno: boolean;
  asmGsm: boolean;
  includeRetired: boolean;
  sort: SortState;
  page: number;
  pageSize: number;
}

export const defaultSort: SortState = { field: 'symbol', descending: false };
export const defaultPageSize = 50;

export const emptyFilters: InstrumentFilters = {
  q: '',
  type: null,
  sector: null,
  series: null,
  fno: false,
  asmGsm: false,
  includeRetired: false,
  sort: defaultSort,
  page: 1,
  pageSize: defaultPageSize,
};

const isType = (value: string | null): value is InstrumentType => instrumentTypes.some((t) => t === value);
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
export function parseFilters(params: URLSearchParams): InstrumentFilters {
  const type = params.get('type');
  const size = positiveInt(params.get('size'), defaultPageSize);
  return {
    q: params.get('q') ?? '',
    type: isType(type) ? type : null,
    sector: nonEmpty(params.get('sector')),
    series: nonEmpty(params.get('series')),
    fno: params.get('fno') === '1',
    asmGsm: params.get('asm') === '1',
    includeRetired: params.get('retired') === '1',
    sort: parseSort(params.get('sort')),
    page: positiveInt(params.get('page'), 1),
    pageSize: pageSizes.some((s) => s === size) ? size : defaultPageSize,
  };
}

/** Only non-default values are written, so the URL of the plain list stays `/instruments`. */
export function toParams(filters: InstrumentFilters, extra: URLSearchParams = new URLSearchParams()): URLSearchParams {
  const next = new URLSearchParams(extra);
  const set = (key: string, value: string | null) => {
    if (value) next.set(key, value);
    else next.delete(key);
  };
  set('q', filters.q.trim() || null);
  set('type', filters.type);
  set('sector', filters.sector);
  set('series', filters.series);
  set('fno', filters.fno ? '1' : null);
  set('asm', filters.asmGsm ? '1' : null);
  set('retired', filters.includeRetired ? '1' : null);
  set('sort', formatSort(filters.sort) === formatSort(defaultSort) ? null : formatSort(filters.sort));
  set('page', filters.page > 1 ? String(filters.page) : null);
  set('size', filters.pageSize !== defaultPageSize ? String(filters.pageSize) : null);
  return next;
}

/** Number of narrowing filters in use (search text, type, sector, series, F&O, ASM/GSM, retired). */
export function activeFilterCount(f: InstrumentFilters): number {
  return [f.q.trim() !== '', f.type, f.sector, f.series, f.fno, f.asmGsm, f.includeRetired].filter(Boolean).length;
}
