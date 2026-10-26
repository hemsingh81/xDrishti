import './shim';
import { http, HttpResponse } from 'msw';
import type { Schemas } from '../src/shared/api';
import { buildCompare, buildThinCompare, mountCompare } from '../src/test/compareFixtures';
import { buildCounterfactual, buildJourney, journeyOf, mountJourney } from '../src/test/journeyFixtures';
import {
  buildCell,
  buildDigest,
  buildEntry,
  buildExperiment,
  buildGradedPlanVsActual,
  buildHypothesis,
  buildLesson,
  buildModel,
  buildModelDetail,
  buildOverview,
  buildThinCell,
  mountLearning,
  mountRecords,
} from '../src/test/learningFixtures';
import { mountPaper } from '../src/test/paperFixtures';
import { buildDiscipline, buildReview, mountReview } from '../src/test/paperReviewFixtures';
import { buildPhase, mountPhase } from '../src/test/phaseFixtures';
import { mount as mountPlans } from '../src/test/planScreen';
import { rankedWhatWorks, buildRunsView } from '../src/test/reportFixtures';
import { reportServer } from '../src/test/reportScreen';
import { problemResponse, server } from '../src/test/server';
import type { State } from './checks/types';

export const widths = [360, 768, 1440, 2560] as const;
export const states: readonly State[] = ['loading', 'error', 'empty', 'thin', 'full', 'unavailable'];

export interface Screen {
  /** Spec row (P8-xx) and a short name: the id in the result file. */
  id: string;
  route: string;
  /** Review-type screens have their answer checked above the fold at 360 x 800 (C2). */
  review?: boolean;
  /** Mounts the fixtures of a state; a state not listed has no fixture and is recorded `na`. */
  mounts: Partial<Record<State, () => void>>;
}

type Cell = ReturnType<typeof buildCell>;
const cells = (): Cell[] => [
  buildCell(),
  buildCell({ strategy: 'vwap', timeframe: '5m', expectancyR: -0.12 }),
  buildCell({ strategy: 'orb', timeframe: '5m', side: 'SELL' }),
  buildThinCell({ strategy: 'gap' }),
];
const learningFull = () => {
  mountLearning({
    overview: buildOverview({ cellRows: 12, cellsAsOf: '2026-10-20' }),
    cells: { all: cells(), '250d': cells(), '60d': cells() },
    changelog: [buildEntry()],
    planVsActual: buildGradedPlanVsActual(),
  });
};
const learningThin = () => {
  mountLearning({
    overview: buildOverview({ cellRows: 2, cellsAsOf: '2026-10-20' }),
    cells: { all: [buildThinCell(), buildThinCell({ strategy: 'vwap' })] },
  });
};
const learningEmpty = () => {
  mountLearning({});
};
const records = () => {
  mountRecords({
    lessons: [buildLesson(), buildLesson({ id: 2 })],
    experiments: [buildExperiment(), buildExperiment({ id: 2 })],
    hypotheses: [buildHypothesis()],
    models: [buildModel()],
    modelDetails: [buildModelDetail()],
    digests: [buildDigest()],
  });
  learningFull();
};
const recordsEmpty = () => {
  mountRecords({});
  learningEmpty();
};

const learning = (id: string, tab: string, mounts: Screen['mounts'], review = false): Screen => ({
  id: `P8-${id}`,
  route: tab ? `/learning?tab=${tab}` : '/learning',
  review,
  mounts,
});

const reportBase = () => {
  reportServer({ whatWorks: rankedWhatWorks() });
  server.use(
    http.get('/api/reports/benchmarks', () => problemResponse({ title: 'Not found', code: 'reports.not_found' }, 404)),
  );
};
const reportEmpty = () => {
  reportServer({ runs: buildRunsView([]) });
  server.use(
    http.get('/api/reports/benchmarks', () => problemResponse({ title: 'Not found', code: 'reports.not_found' }, 404)),
  );
};
const reports = (id: string, view: string, review = false): Screen => ({
  id: `P8-${id}`,
  route: view ? `/reports?view=${view}` : '/reports',
  review,
  mounts: { full: reportBase, empty: reportEmpty },
});

type AuditPage = Schemas['AuditPageView'];
const auditPage = (items: AuditPage['items']): AuditPage => ({
  window: { from: '2026-09-13', to: '2026-10-12' },
  items,
  nextCursor: null,
  sources: ['auth', 'account', 'approval', 'plan', 'autonomy', 'learning'].map((source) => ({
    source,
    state: 'value',
    reason: null,
  })),
  limits: ['Only what is recorded is shown.'],
});
const auditEntry = (n: number): AuditPage['items'][number] => ({
  id: `account:00000000-0000-4000-8000-0000000000${String(n).padStart(2, '0')}`,
  at: '2026-10-12T05:00:00Z',
  source: 'account',
  category: 'portfolio_data',
  kind: 'HoldingsImported',
  outcome: 'ok',
  actorKind: 'owner',
  actor: 'owner@example.com',
  accountId: null,
  accountName: null,
  summary: `Holdings imported: ${n} holdings imported`,
  redacted: false,
  planId: null,
  itemId: null,
});
const mountAudit = (items: AuditPage['items']) => () => {
  server.use(
    http.get('/api/audit/entries', () => HttpResponse.json(auditPage(items))),
    http.get('/api/audit/summary', () =>
      HttpResponse.json({
        window: { from: '2026-09-13', to: '2026-10-12' },
        total: items.length,
        bySource: [],
        byCategory: [],
        byOutcome: [],
        sources: [],
      }),
    ),
    http.get('/api/audit/kinds', () =>
      HttpResponse.json({
        kinds: [{ source: 'auth', kind: 'LoginFailed', label: 'Login failed', category: 'sign_in', outcome: 'failed' }],
        categories: ['sign_in', 'security'],
        outcomes: ['ok', 'failed', 'refused'],
        actors: ['owner', 'system', 'rule', 'cli', 'other'],
      }),
    ),
  );
};

/** The built P8 screens the runner covers (P8-02 to P8-04, P8-08, P8-09, P8-12, plans, audit, paper). */
export const screens: Screen[] = [
  learning('02-overview', '', { full: learningFull, empty: learningEmpty, thin: learningThin }, true),
  learning('02-setups', 'setups', { full: learningFull, empty: learningEmpty, thin: learningThin }),
  learning('03-lessons', 'lessons', { full: records, empty: recordsEmpty }),
  learning('03-experiments', 'experiments', { full: records, empty: recordsEmpty }),
  learning('03-models', 'models', { full: records, empty: recordsEmpty }),
  learning('03-digest', 'digest', { full: records, empty: recordsEmpty }),
  learning('04-plan', 'plan', { full: learningFull, empty: learningEmpty }),
  learning('04-decisions', 'decisions', { full: learningFull, empty: learningEmpty }),
  learning('04-autonomy', 'autonomy', { full: learningFull }),
  learning('04-checks', 'checks', { full: learningFull }),
  learning('12-journey', 'journey', {
    full: () => {
      mountJourney(journeyOf(buildCounterfactual()), buildCounterfactual());
    },
    empty: () => {
      mountJourney(buildJourney());
    },
  }),
  reports('08-overview', '', false),
  reports('08-what-works', 'what-works', true),
  reports('09-baskets', 'baskets'),
  reports('09-pivot', 'pivot'),
  reports('10-strategies', 'strategies'),
  reports('10-distribution', 'distribution'),
  {
    id: 'P8-14-plans',
    route: '/plans',
    review: true,
    mounts: {
      full: () => {
        mountPlans();
      },
      empty: () => undefined,
    },
  },
  {
    id: 'audit',
    route: '/audit',
    mounts: { full: mountAudit([1, 2, 3, 4, 5].map(auditEntry)), empty: mountAudit([]) },
  },
  {
    id: 'paper-ledger',
    route: '/paper',
    mounts: {
      full: () => {
        mountPaper();
      },
    },
  },
  {
    id: 'paper-review',
    route: '/paper?tab=review',
    mounts: {
      full: () => {
        mountPaper();
        mountReview({ now: buildReview(), discipline: buildDiscipline() });
      },
    },
  },
  {
    id: 'paper-compare',
    route: '/paper?tab=compare',
    mounts: {
      full: () => {
        mountPaper();
        mountCompare(buildCompare());
      },
      thin: () => {
        mountPaper();
        mountCompare(buildThinCompare());
      },
    },
  },
  {
    id: 'paper-phase',
    route: '/paper?tab=phase',
    mounts: {
      full: () => {
        mountPaper();
        mountPhase(buildPhase());
      },
    },
  },
];
