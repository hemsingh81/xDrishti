import { http, HttpResponse } from 'msw';
import { basketIds, seededBaskets } from '../src/test/fixtures';
import { buildProfile, buildRanking, buildRichProfile } from '../src/test/profileFixtures';
import { server } from '../src/test/server';
import type { Screen } from './matrix';

/** The BK-07 screens: the basket profile (full, thin, error) and the ranking board. */
export const basketScreens: Screen[] = [
  {
    id: 'BK-07-profile',
    route: `/baskets/${basketIds.myChoice}/profile`,
    mounts: {
      full: () => {
        server.use(http.get('/api/reports/baskets/:id/profile', () => HttpResponse.json(buildRichProfile())));
      },
      thin: () => {
        server.use(http.get('/api/reports/baskets/:id/profile', () => HttpResponse.json(buildProfile())));
      },
      error: () => {
        server.use(
          http.get('/api/reports/baskets/:id/profile', () =>
            HttpResponse.json({ title: 'The profile failed.', code: 'reports.invalid' }, { status: 422 }),
          ),
        );
      },
    },
  },
  {
    id: 'BK-07-ranking',
    route: `/baskets/ranking?basket=${basketIds.myChoice},${basketIds.myLongTerm}`,
    mounts: {
      full: () => {
        server.use(
          http.get('/api/baskets', () => HttpResponse.json(seededBaskets())),
          http.get('/api/reports/baskets/ranking', () => HttpResponse.json(buildRanking())),
        );
      },
    },
  },
];
