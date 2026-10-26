import './shim';
import { http, HttpResponse } from 'msw';
import { buildCompare, buildThinCompare, mountCompare } from '../src/test/compareFixtures';
import { buildCounterfactual, buildJourney, journeyOf, mountJourney } from '../src/test/journeyFixtures';
import { createCapabilitiesFake } from '../src/test/capabilitiesFake';
import { createChargeSheetFake } from '../src/test/chargeSheetFake';
import { lossTags, rule, smart } from '../src/test/evidenceFixtures';
import { mountPaper } from '../src/test/paperFixtures';
import { buildDiscipline, buildReview, mountReview } from '../src/test/paperReviewFixtures';
import { buildPhase, mountPhase } from '../src/test/phaseFixtures';
import { mount as mountPlans } from '../src/test/planScreen';
import { replayServer } from '../src/test/replayFixtures';
import { runKeyA } from '../src/test/reportFixtures';
import { problemResponse, server } from '../src/test/server';
import { learningEmpty, learningFull, learningThin, records, recordsEmpty } from './learningMounts';
import { mountTrades, reportBase, reportEmpty } from './reportMounts';
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

const learning = (id: string, tab: string, mounts: Screen['mounts'], review = false): Screen => ({
  id: `P8-${id}`,
  route: tab ? `/learning?tab=${tab}` : '/learning',
  review,
  mounts,
});

const noStudies = () => {
  server.use(
    http.get('/api/learning/entry-windows', () =>
      problemResponse({ title: 'None stored.', code: 'learning.entry_windows_not_found' }, 404),
    ),
    http.get('/api/learning/meta-policy', () =>
      problemResponse({ title: 'None stored.', code: 'learning.meta_policy_not_found' }, 404),
    ),
  );
};
const smartRulesFull = () => {
  learningFull();
  noStudies();
  server.use(
    http.get('/api/learning/smart-rules', () =>
      HttpResponse.json(
        smart({
          state: 'observed',
          plansRead: 12,
          rules: [
            rule('gate', 'Market gate', { state: 'observed', ranOn: 10, unavailableOn: 2 }),
            rule('throttle', 'Equity throttle'),
          ],
        }),
      ),
    ),
  );
};
const smartRulesEmpty = () => {
  learningFull();
  noStudies();
  server.use(http.get('/api/learning/smart-rules', () => HttpResponse.json(smart())));
};
const lossTagsMount = (view: Parameters<typeof lossTags>[0]) => () => {
  learningFull();
  noStudies();
  server.use(http.get('/api/learning/loss-tags', () => HttpResponse.json(lossTags(view))));
};

const reports = (id: string, view: string, review = false, scrollToEnd = false, extra?: () => void): Screen => ({
  id: `P8-${id}`,
  route: view ? `/reports?view=${view}` : '/reports',
  review,
  scrollToEnd,
  mounts: {
    full: () => {
      reportBase();
      extra?.();
    },
    empty: reportEmpty,
  },
});

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
  reports('10-side', 'side'),
  reports('10-calendar', 'calendar'),
  reports('10-maemfe', 'maemfe'),
  // Exits and Monte Carlo are answered with empty lists here (their rich builders live inside their test files): the checks cover the
  // screen, its states and its layout, not a populated comparison table.
  reports('10-exits', 'exits', false, false, () => {
    server.use(
      http.get('/api/reports/comparisons', () => HttpResponse.json({ comparisons: [], limits: [] })),
      http.get('/api/reports/exit-selections', () => HttpResponse.json({ selections: [], limits: [] })),
    );
  }),
  reports('10-montecarlo', 'montecarlo', false, false, () => {
    server.use(http.get('/api/lab/runs', () => HttpResponse.json([])));
  }),
  reports('10-instruments', 'instruments'),
  reports('10-sectors', 'sectors'),
  learning('13-smart-rules', 'smart-rules', { full: smartRulesFull, empty: smartRulesEmpty }),
  learning('13-loss-tags', 'loss-tags', {
    full: lossTagsMount({}),
    empty: lossTagsMount({ state: 'empty', tradesRead: 0, losses: 0, cells: [], worst: [] }),
  }),
  {
    id: 'P8-18-owner-acts',
    route: '/owner-acts',
    mounts: {
      full: () => {
        server.use(...createCapabilitiesFake().handlers);
      },
    },
  },
  {
    id: 'settings-charge-sheet',
    route: '/settings?tab=charge-sheet',
    mounts: {
      full: () => {
        server.use(...createChargeSheetFake().handlers);
      },
    },
  },
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
