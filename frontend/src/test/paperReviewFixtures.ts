import { http, HttpResponse } from 'msw';
import type { Schemas } from '@/shared/api';
import { server } from './server';

type Review = Schemas['PaperReviewView'];
type Discipline = Schemas['DisciplineView'];
type Line = Schemas['DisciplineLineView'];

const gone = (state: string, reason: string) => ({ state, number: null, reason });
const value = (number: number) => ({ state: 'value', number, reason: null });

export const reviewPinned = 'Paper only: no order was placed and none exists.';

export const buildReview = (over: Partial<Review> = {}): Review => ({
  id: null,
  week: '2026-W43',
  epochId: 1,
  builtAt: '2026-10-24T10:30:00+05:30',
  builtBy: null,
  stored: false,
  repeated: false,
  text: `${reviewPinned}\n`,
  sections: [
    {
      id: 'standing',
      title: 'Where it stands',
      lines: [
        'Day 20 of 84; week 3 of 12.',
        'ETA to 60: unavailable: needs at least 10 counted trades and 3 weeks elapsed.',
      ],
      metrics: [],
      limits: 'An ETA is a straight-line guess, not a forecast.',
      table: null,
    },
    {
      id: 'week',
      title: 'This week',
      lines: ['Closed paper trades this window: 1 (one row per account).', '- 2026-10-21 TCS SELL 5 +1.50R'],
      metrics: [],
      limits: 'Fills are the simulator’s.',
      table: {
        columns: ['Day', 'Symbol', 'Side', 'Qty', 'R', 'Rupees (after stored charges)', 'Exit'],
        rows: [['2026-10-21', 'TCS', 'SELL', '5', '+1.50R', '+750.00', 'target']],
      },
    },
    {
      id: 'comparison',
      title: 'Backtest versus paper',
      lines: ['unavailable: the backtest comparison (P10-04) is not built, so no position against the band is given.'],
      metrics: [],
      limits: 'One line only.',
      table: null,
    },
  ],
  ...over,
});

const line = (over: Partial<Line>): Line => ({
  category: 'followed',
  kind: 'category',
  title: 'Followed: approved at full size, entered and closed',
  n: 6,
  sumR: value(1),
  rMetric: gone('insufficient', 'n 6 < 15'),
  rupees: [{ accountId: '11111111-1111-4111-8111-111111111111', amount: 1200 }],
  note: '3 wins of 6',
  ...over,
});

export const buildDiscipline = (over: Partial<Discipline> = {}): Discipline => ({
  basis: 'paper only: skipped tickets are graded on plan prices (simulated on plan prices)',
  epochId: 1,
  from: '2026-10-01',
  to: '2026-10-23',
  lines: [
    line({}),
    line({
      category: 'rejected',
      title: 'Rejected',
      n: 2,
      sumR: value(1.5),
      rupees: [],
      note: 'graded 1, not_triggered 1, not_graded 0',
    }),
    line({
      category: 'auto_approved',
      kind: 'flag',
      title: 'Approved automatically',
      n: 0,
      sumR: gone('empty', 'a count only'),
      rMetric: gone('empty', 'a count only'),
      rupees: [],
      note: '',
    }),
    line({
      category: 'added',
      kind: 'deviation',
      title: 'Added: real fills with no ticket',
      n: 0,
      sumR: gone('empty', 'no real fills in the window'),
      rMetric: gone('empty', 'no real fills in the window'),
      rupees: [],
      note: 'empty: no real fills in the window',
    }),
  ],
  execution: [
    { id: 'plan', label: 'Plan R (exact prices)', value: value(2), n: 1 },
    {
      id: 'missed_triggers',
      label: 'Missed triggers',
      value: gone('not_evaluable', 'paper has no real fills (P11)'),
      n: 0,
    },
  ],
  evidence: { sessions: 15, primaryTickets: 10, closedTrades: 7 },
  unavailable: null,
  limits: ['There is no composite discipline score: none is defined (P8-00 D10).'],
  ...over,
});

export interface ReviewMount {
  now?: Review;
  stored?: Review[];
  discipline?: Discipline;
}

/** Serves the four read-only review endpoints. */
export function mountReview(opts: ReviewMount = {}) {
  const stored = opts.stored ?? [];
  server.use(
    http.get('/api/paper/reviews', () =>
      HttpResponse.json({
        items: stored.map((r, i) => ({ id: i + 1, week: r.week, epochId: 1, builtAt: r.builtAt })),
      }),
    ),
    http.get('/api/paper/reviews/:week', ({ params }) => {
      const found = stored.find((r) => r.week === params.week);
      return found ? HttpResponse.json(found) : new HttpResponse(null, { status: 404 });
    }),
    http.get('/api/paper/review', () => HttpResponse.json(opts.now ?? buildReview())),
    http.get('/api/paper/discipline', () => HttpResponse.json(opts.discipline ?? buildDiscipline())),
  );
}
