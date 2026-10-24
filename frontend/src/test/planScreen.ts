import { http, HttpResponse } from 'msw';
import { buildPlan, buildReview, planItems } from './planFixtures';
import { problemResponse, server } from './server';

export const wording = 'Approved for watching: no order is placed and none exists.';
export const detailPlan = () => buildPlan({ items: planItems() });

export interface Options {
  review?: ReturnType<typeof buildReview>;
  autoApprove?: boolean;
}

/** Mounts the screen over a published plan; returns the bodies the tests posted. */
export function mount(opts: Options = {}) {
  const posted: Record<string, unknown> = {};
  server.use(
    http.get('/api/plans', () => HttpResponse.json([buildPlan()])),
    http.get('/api/plans/7', () => HttpResponse.json(detailPlan())),
    http.get('/api/plans/7/review', () => HttpResponse.json(opts.review ?? buildReview())),
    http.get('/api/plans/7/suggestions', () =>
      HttpResponse.json([
        {
          id: 5,
          kind: 'open_book_unavailable',
          subject: 'plan',
          text: 'Open book is unavailable.',
          createdAt: '2026-10-25T16:00:00Z',
          seen: false,
        },
      ]),
    ),
    http.get('/api/plans/7/grade', () =>
      HttpResponse.json({
        planId: 7,
        forDate: '2026-10-26',
        basis: 'simulated on plan prices',
        tickets: [],
        pending: 3,
        report: {},
      }),
    ),
    http.get('/api/plans/7/journal', () =>
      HttpResponse.json({
        planId: 7,
        forDate: '2026-10-26',
        fillBasis: 'realised fills, not simulated',
        planBasis: 'simulated on plan prices',
        entries: [],
        discretionary: [],
        ticketsWithoutFills: 0,
      }),
    ),
    http.get('/api/learning/digests', () =>
      HttpResponse.json([
        {
          id: 1,
          isoWeek: '2026-W43',
          weekStart: '2026-10-19',
          weekEnd: '2026-10-25',
          sections: [{ key: 'cells', title: 'Cells', lines: ['Cell orb is drifting.'] }],
          text: '',
          builtBy: 'rule:weekly',
          createdAt: null,
          repeated: false,
        },
      ]),
    ),
    http.get('/api/autonomy/settings', () =>
      HttpResponse.json({
        id: null,
        hash: null,
        autoApproveTickets: opts.autoApprove ?? false,
        promoteFromReserve: false,
        minPWin: 0.6,
        maxAutoPerDay: 3,
        notice: 'No order is placed in any setting.',
        history: [],
      }),
    ),
  );
  return posted;
}

export function capture(path: string, body: object, status = 200) {
  const seen: { body?: unknown } = {};
  server.use(
    http.post(path, async ({ request }) => {
      seen.body = await request.json().catch(() => undefined);
      return status === 200 ? HttpResponse.json(body) : problemResponse(body, status);
    }),
  );
  return seen;
}
