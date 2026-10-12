/** The search state of the Instruments page. It lives in the URL, so a view can be shared and Back works. */
export const instrumentTypes = ['share', 'etf', 'index', 'future'] as const;
export type InstrumentType = (typeof instrumentTypes)[number];

/** The two lists of the screen: everything Dhan lists, or only what the owner tracks. */
export const instrumentTabs = ['master', 'tracked'] as const;
export type InstrumentTab = (typeof instrumentTabs)[number];

export const sortFields = ['symbol', 'name', 'type', 'series', 'sector', 'lot', 'tick'] as const;
export type SortField = (typeof sortFields)[number];

export interface SortState {
  field: SortField;
  descending: boolean;
}

export interface InstrumentFilters {
  tab: InstrumentTab;
  q: string;
  type: InstrumentType | null;
  sector: string | null;
  series: string | null;
  fno: boolean;
  asmGsm: boolean;
  includeRetired: boolean;
  /** Master list only: the instruments that are not tracked. */
  untracked: boolean;
  sort: SortState;
}

export const defaultSort: SortState = { field: 'symbol', descending: false };

export const emptyFilters: InstrumentFilters = {
  tab: 'master',
  q: '',
  type: null,
  sector: null,
  series: null,
  fno: false,
  asmGsm: false,
  includeRetired: false,
  untracked: false,
  sort: defaultSort,
};

const isTab = (value: string | null): value is InstrumentTab => instrumentTabs.some((t) => t === value);
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

/** Reads the page state from URL parameters; anything unknown or malformed falls back to the default. */
export function parseFilters(params: URLSearchParams): InstrumentFilters {
  const type = params.get('type');
  const tab = params.get('tab');
  return {
    tab: isTab(tab) ? tab : 'master',
    q: params.get('q') ?? '',
    type: isType(type) ? type : null,
    sector: nonEmpty(params.get('sector')),
    series: nonEmpty(params.get('series')),
    fno: params.get('fno') === '1',
    asmGsm: params.get('asm') === '1',
    includeRetired: params.get('retired') === '1',
    untracked: params.get('untracked') === '1',
    sort: parseSort(params.get('sort')),
  };
}

/** Only non-default values are written, so the URL of the plain list stays `/instruments`. */
export function toParams(filters: InstrumentFilters, extra: URLSearchParams = new URLSearchParams()): URLSearchParams {
  const next = new URLSearchParams(extra);
  const set = (key: string, value: string | null) => {
    if (value) next.set(key, value);
    else next.delete(key);
  };
  set('tab', filters.tab === 'master' ? null : filters.tab);
  set('q', filters.q.trim() || null);
  set('type', filters.type);
  set('sector', filters.sector);
  set('series', filters.series);
  set('fno', filters.fno ? '1' : null);
  set('asm', filters.asmGsm ? '1' : null);
  set('retired', filters.includeRetired ? '1' : null);
  set('untracked', filters.untracked ? '1' : null);
  set('sort', formatSort(filters.sort) === formatSort(defaultSort) ? null : formatSort(filters.sort));
  // The list loads as the owner scrolls: an old link's page and size are dropped.
  set('page', null);
  set('size', null);
  return next;
}

/**
 * Number of narrowing filters in use (search text, type, sector, series, F&O, ASM/GSM, and on the master list retired and
 * not tracked). The tab is not a filter.
 */
export function activeFilterCount(f: InstrumentFilters): number {
  const masterOnly = f.tab === 'master' ? [f.includeRetired, f.untracked] : [];
  return [f.q.trim() !== '', f.type, f.sector, f.series, f.fno, f.asmGsm, ...masterOnly].filter(Boolean).length;
}

/** Identifies the list a selection was made in: the tab and the narrowing filters (not the sort). */
export const selectionScope = (f: InstrumentFilters): string =>
  JSON.stringify([f.tab, f.q.trim(), f.type, f.sector, f.series, f.fno, f.asmGsm, f.includeRetired, f.untracked]);
