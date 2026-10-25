import { queryOptions } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type LearningOverview = Schemas['LearningOverviewView'];
export type OverviewSection = Schemas['OverviewSection'];
export type CellStats = Schemas['CellStatsView'];
export type ChangelogEntry = Schemas['ChangelogEntryView'];
export type CellState = Schemas['CellStateView'];
export type Lesson = Schemas['LessonView'];
export type Experiment = Schemas['ExperimentView'];
export type Hypothesis = Schemas['HypothesisView'];
export type Model = Schemas['ModelView'];
export type ModelDetail = Schemas['ModelDetailView'];
export type Digest = Schemas['DigestView'];
export type Counterfactual = Schemas['CounterfactualReportView'];
export type CounterfactualWindow = Schemas['CounterfactualWindowView'];
export type Journey = Schemas['JourneyView'];

export type PlanVsActual = Schemas['PlanVsActualView'];
export type MetricView = Schemas['MetricView'];
export type DecisionsReport = Schemas['GradeReportData'];
export type GradeStats = Schemas['GradeStats'];
export type AutonomySettings = Schemas['AutonomySettingsView'];
export type PlanPhaseCheck = Schemas['PlanPhaseCheckView'];
export type LearningPhaseCheck = Schemas['LearningPhaseCheckView'];
export type SmartRules = Schemas['SmartRulesView'];
export type SmartRule = Schemas['SmartRuleView'];
export type LossTags = Schemas['LossTagsView'];
export type CellLossTags = Schemas['CellLossTagsView'];
export type EntryWindowStudy = Schemas['EntryWindowReportView'];
export type MetaPolicyStudy = Schemas['MetaPolicyReportView'];

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
  lessons: (state: string) => ['learning', 'lessons', state] as const,
  lesson: (id: number) => ['learning', 'lesson', id] as const,
  experiments: ['learning', 'experiments'] as const,
  experiment: (id: number) => ['learning', 'experiment', id] as const,
  hypotheses: (state: string) => ['learning', 'hypotheses', state] as const,
  hypothesis: (id: number) => ['learning', 'hypothesis', id] as const,
  models: (role: string) => ['learning', 'models', role] as const,
  model: (id: number) => ['learning', 'model', id] as const,
  digests: ['learning', 'digests'] as const,
  journey: ['learning', 'journey'] as const,
  counterfactual: (id: number) => ['learning', 'counterfactual', id] as const,
  digest: (week: string) => ['learning', 'digest', week] as const,

  planVsActual: (range: DateRange) => ['learning', 'plan-vs-actual', range.from, range.to] as const,
  decisions: (range: DateRange) => ['learning', 'decisions', range.from, range.to] as const,
  autonomy: ['learning', 'autonomy'] as const,
  planCheck: ['learning', 'plan-phase-check'] as const,
  learningCheck: ['learning', 'learning-phase-check'] as const,
  smartRules: ['learning', 'smart-rules'] as const,
  entryWindows: ['learning', 'entry-windows'] as const,
  metaPolicy: ['learning', 'meta-policy'] as const,
  lossTags: (runs: readonly string[]) => ['learning', 'loss-tags', ...runs] as const,
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

// P8-03: the record lists and their details. Lists ask for the newest 50; a detail is read only when its drawer opens.
const recordStaleTime = 60_000;
export const recordLimit = 50;
export const digestLimit = 12;
const all = (filter: string) => (filter === 'all' ? {} : { state: filter });

export const lessonsQuery = (state: string) =>
  queryOptions({
    queryKey: learningKeys.lessons(state),
    staleTime: recordStaleTime,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/lessons', { params: { query: { ...all(state), limit: recordLimit } }, signal })),
  });

export const lessonQuery = (id: number) =>
  queryOptions({
    queryKey: learningKeys.lesson(id),
    staleTime: recordStaleTime,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/lessons/{id}', { params: { path: { id } }, signal })),
  });

export const experimentsQuery = queryOptions({
  queryKey: learningKeys.experiments,
  staleTime: recordStaleTime,
  queryFn: async ({ signal }) =>
    unwrap(await api.GET('/api/experiments', { params: { query: { limit: recordLimit } }, signal })),
});

export const experimentQuery = (id: number) =>
  queryOptions({
    queryKey: learningKeys.experiment(id),
    staleTime: recordStaleTime,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/experiments/{id}', { params: { path: { id } }, signal })),
  });

export const hypothesesQuery = (state: string) =>
  queryOptions({
    queryKey: learningKeys.hypotheses(state),
    staleTime: recordStaleTime,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/hypotheses', { params: { query: { ...all(state), limit: recordLimit } }, signal })),
  });

export const hypothesisQuery = (id: number) =>
  queryOptions({
    queryKey: learningKeys.hypothesis(id),
    staleTime: recordStaleTime,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/hypotheses/{id}', { params: { path: { id } }, signal })),
  });

export const modelsQuery = (role: string) =>
  queryOptions({
    queryKey: learningKeys.models(role),
    staleTime: recordStaleTime,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/models', {
          params: { query: { ...(role === 'all' ? {} : { role }), limit: recordLimit } },
          signal,
        }),
      ),
  });

export const modelQuery = (id: number) =>
  queryOptions({
    queryKey: learningKeys.model(id),
    staleTime: recordStaleTime,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/models/{id}', { params: { path: { id } }, signal })),
  });

export const digestsQuery = queryOptions({
  queryKey: learningKeys.digests,
  staleTime: recordStaleTime,
  queryFn: async ({ signal }) =>
    unwrap(await api.GET('/api/learning/digests', { params: { query: { limit: digestLimit } }, signal })),
});

export const digestQuery = (week: string) =>
  queryOptions({
    queryKey: learningKeys.digest(week),
    staleTime: recordStaleTime,
    retry: false,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/learning/digest', { params: { query: { week } }, signal })),
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

// P8-12: the Journey. The counterfactual is computed and stored by the verb; these only read it.
export const journeyQuery = queryOptions({
  queryKey: learningKeys.journey,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/learning/journey', { signal })),
});

export const counterfactualQuery = (id: number) =>
  queryOptions({
    queryKey: learningKeys.counterfactual(id),
    staleTime: recordStaleTime,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/learning/counterfactual', { params: { query: { id } }, signal })),
  });

/** What the stored plans say about each smart rule (P8-13), over the newest sessions the server reads by default. */
export const smartRulesQuery = queryOptions({
  queryKey: learningKeys.smartRules,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/learning/smart-rules', { signal })),
});

/** Loss tags on the default run set, or on the runs named as `KEY:STYLE:EXIT`. */
export const lossTagsQuery = (runs: readonly string[]) =>
  queryOptions({
    queryKey: learningKeys.lossTags(runs),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/learning/loss-tags', { params: { query: { run: [...runs] } }, signal })),
  });

// P8-13b: the entry-window study and the meta-policy evaluation are computed and stored by the owner's verbs; these only read the newest.
// A 404 means none is stored (the tab shows the command), so there is no retry.
export const entryWindowsQuery = queryOptions({
  queryKey: learningKeys.entryWindows,
  retry: false,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/learning/entry-windows', { signal })),
});

export const metaPolicyQuery = queryOptions({
  queryKey: learningKeys.metaPolicy,
  retry: false,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/learning/meta-policy', { signal })),
});
