import { queryOptions } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type LearningOverview = Schemas['LearningOverviewView'];
export type OverviewSection = Schemas['OverviewSection'];
export type CellStats = Schemas['CellStatsView'];
export type ChangelogEntry = Schemas['ChangelogEntryView'];
export type CellState = Schemas['CellStateView'];

/** The windows the server computes for every cell (`CellWindow` codes). */
export const cellWindows = ['all', '250d', '60d'] as const;
export type CellWindow = (typeof cellWindows)[number];
export const windowLabels: Record<CellWindow, string> = { all: 'All', '250d': 'Last 250 days', '60d': 'Last 60 days' };

/** The most changelog rows one request returns (the server's cap). */
export const changelogLimit = 200;

/** Which changelog rows: all, those of cells, or those of one cell (`subject` is the cell key). */
export interface ChangelogScope {
  subjectKind?: 'cell';
  subject?: string;
}

export const learningKeys = {
  overview: ['learning', 'overview'] as const,
  cells: (window: CellWindow) => ['learning', 'cells', window] as const,
  cell: (key: string) => ['learning', 'cell', key] as const,
  changelog: (scope: ChangelogScope) =>
    ['learning', 'changelog', scope.subjectKind ?? null, scope.subject ?? null] as const,
  cellState: (cell: string) => ['learning', 'cell-state', cell] as const,
};

// All five are reads. This feature calls no write (see readOnly.test.ts).
export const overviewQuery = queryOptions({
  queryKey: learningKeys.overview,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/learning/overview', { signal })),
});

export const cellsQuery = (window: CellWindow) =>
  queryOptions({
    queryKey: learningKeys.cells(window),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/cells', { params: { query: { window } }, signal })),
  });

/** One cell in its windows, from the newest snapshot that holds it. */
export const cellQuery = (key: string) =>
  queryOptions({
    queryKey: learningKeys.cell(key),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/cells/{key}', { params: { path: { key } }, signal })),
  });

/** The newest changelog rows (at most `changelogLimit`), narrowed by `scope`. */
export const changelogQuery = (scope: ChangelogScope = {}) =>
  queryOptions({
    queryKey: learningKeys.changelog(scope),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/changelog', { params: { query: { ...scope, limit: changelogLimit } }, signal })),
  });

/** Where one cell is, replayed by the server from its changelog rows. */
export const cellStateQuery = (cell: string) =>
  queryOptions({
    queryKey: learningKeys.cellState(cell),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/changelog/cell-state', { params: { query: { cell } }, signal })),
  });
