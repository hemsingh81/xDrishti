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
import { replayServer } from '../src/test/replayFixtures';
import { buildEvidence, buildRunsView, rankedWhatWorks, runKeyA } from '../src/test/reportFixtures';
import { reportServer } from '../src/test/reportScreen';
import { problemResponse, server } from '../src/test/server';
import { auditEntry, mountAudit } from './auditFixtures';
import type { State } from './checks/types';

export const widths = [360, 768, 1440, 2560] as const;
export const states: readonly State[] = ['loading', 'error', 'empty', 'thin', 'full', 'unavailable'];

export interface Screen {
  /** Spec row (P8-xx) and a short name: the id in the result file. */
  id: string;
  route: string;
  /** Review-type screens have their answer checked above the fold at 360 x 800 (C2). */
  review?: boolean;
  /** Panels below the first view mount when near the viewport (DeferUntilNear): the runner scrolls to the end and back. */
  scrollToEnd?: boolean;
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
const reports = (id: string, view: string, review = false, scrollToEnd = false): Screen => ({
  id: `P8-${id}`,
  route: view ? `/reports?view=${view}` : '/reports',
  review,
  scrollToEnd,
  mounts: { full: reportBase, empty: reportEmpty },
});

const tradeLine = (ordinal: number): Schemas['TradeLineView'] => ({
  runKey: runKeyA,
  tradeKey: `ORB|NSE:S${String(ordinal)}|BUY|2026-03-02`,
  strategy: 'ORB',
  timeframe: '15m',
  side: 'BUY',
  symbol: `S${String(ordinal)}`,
  basketIds: [],
  entryDate: '2026-03-02',
  exitDate: '2026-03-03',
  r: ordinal % 2 === 0 ? 1.5 : -1,
  sessionsHeld: 1,
  style: 'intraday',
  exit: 'fixed_r',
  provisional: false,
  ordinal,
});
const mountTrades = (count: number) => () => {
  reportBase();
  const items = Array.from({ length: count }, (_, i) => tradeLine(i + 1));
  server.use(
    http.get('/api/reports/trades', () =>
      HttpResponse.json({
        items,
        total: count,
        nextCursor: null,
        evidence: buildEvidence({ n: count }),
      } satisfies Schemas['TradesView']),
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
  reports('08-overview', '', false, true),
  reports('08-what-works', 'what-works', true),
  reports('09-baskets', 'baskets'),
  reports('09-pivot', 'pivot'),
  reports('10-strategies', 'strategies'),
  reports('10-distribution', 'distribution'),
  {
    id: 'P8-11-cell',
    route: '/reports/cell?strategy=ORB&timeframe=15m&cell=ORB%20%C2%B7%2015m',
    mounts: { full: mountTrades(12), empty: mountTrades(0) },
  },
  {
    id: 'P8-11-replay',
    route: `/reports/trades/${runKeyA}/4`,
    mounts: {
      full: () => {
        replayServer();
      },
    },
  },
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
