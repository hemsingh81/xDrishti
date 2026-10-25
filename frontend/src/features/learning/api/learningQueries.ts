import { queryOptions } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type LearningOverview = Schemas['LearningOverviewView'];
export type OverviewSection = Schemas['OverviewSection'];
export type CellStats = Schemas['CellStatsView'];
export type ChangelogEntry = Schemas['ChangelogEntryView'];
export type CellState = Schemas['CellStateView'];
export type PlanVsActual = Schemas['PlanVsActualView'];
export type MetricView = Schemas['MetricView'];
export type DecisionsReport = Schemas['GradeReportData'];
export type GradeStats = Schemas['GradeStats'];
export type AutonomySettings = Schemas['AutonomySettingsView'];
export type PlanPhaseCheck = Schemas['PlanPhaseCheckView'];
export type LearningPhaseCheck = Schemas['LearningPhaseCheckView'];

/** The planned-session range of Plan vs actual and Decisions; either end may be absent (all plans). */
export interface DateRange {
  from: string | null;
  to: string | null;
}

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
  planVsActual: (range: DateRange) => ['learning', 'plan-vs-actual', range.from, range.to] as const,
  decisions: (range: DateRange) => ['learning', 'decisions', range.from, range.to] as const,
  autonomy: ['learning', 'autonomy'] as const,
  planCheck: ['learning', 'plan-phase-check'] as const,
  learningCheck: ['learning', 'learning-phase-check'] as const,
};

// All of these are reads. This feature calls no write (see readOnly.test.ts).
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

const rangeQuery = (range: DateRange) => ({ from: range.from ?? undefined, to: range.to ?? undefined });

/** The chain from the plan's R to the realised R; the server computes every figure. */
export const planVsActualQuery = (range: DateRange) =>
  queryOptions({
    queryKey: learningKeys.planVsActual(range),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/reports/plan-vs-actual', { params: { query: rangeQuery(range) }, signal })),
    // The Overview tile reads this on every Learning open and the server joins several stores for it: keep it for 5 minutes.
    staleTime: 5 * 60 * 1000,
  });

/** The decisions report (simulated on plan prices) of the graded plans of a range. */
export const decisionsQuery = (range: DateRange) =>
  queryOptions({
    queryKey: learningKeys.decisions(range),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/reports/decisions', { params: { query: rangeQuery(range) }, signal })),
  });

/** The autonomy switches in force and the history of changes (read only; the change is the owner's, elsewhere). */
export const autonomyQuery = queryOptions({
  queryKey: learningKeys.autonomy,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/autonomy/settings', { signal })),
});

export const planCheckQuery = queryOptions({
  queryKey: learningKeys.planCheck,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/plan-runs/phase-check', { signal })),
});

export const learningCheckQuery = queryOptions({
  queryKey: learningKeys.learningCheck,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/learning/phase-check', { signal })),
});
