import { http, HttpResponse } from 'msw';
import { server } from '../src/test/server';
import type { Screen } from './matrix';

/** The P15-11 screens (filings, the event filter's switch card, the verdicts); the default handlers answer the calendar and the filter. */
export const p15Screens: Screen[] = [
  {
    id: 'P15-11-filings',
    route: '/data?tab=filings',
    mounts: {
      full: () => {
        server.use(
          http.get('/api/filings/coverage', () =>
            HttpResponse.json({ from: '2025-11-02', to: '2026-11-02', trackedInstruments: 30, kinds: [] }),
          ),
          http.get('/api/filings', () =>
            HttpResponse.json({ state: null, unavailableCount: 0, items: [], truncated: false }),
          ),
        );
      },
    },
  },
  {
    id: 'P15-11-event-filter',
    route: '/settings?tab=selection',
    mounts: { full: () => undefined },
  },
  {
    id: 'P15-11-extensions',
    route: '/learning?tab=extensions',
    mounts: {
      full: () => {
        server.use(http.get('/api/research/extensions', () => HttpResponse.json({ family: 'pead', items: [] })));
      },
    },
  },
  {
    id: 'P14-16-options-stat-arb',
    route: '/learning?tab=options',
    mounts: {
      full: () => {
        server.use(
          http.get('/api/research/extensions', () => HttpResponse.json({ family: 'ext:iv-1', items: [] })),
          http.get('/api/research/expression-runs', () =>
            HttpResponse.json({ state: 'unavailable', reason: 'no_runs', items: [] }),
          ),
          http.get('/api/research/pair-formations', () =>
            HttpResponse.json({
              state: 'unavailable',
              reason: 'no_runs',
              from: '2026-01-01',
              to: '2026-10-30',
              items: [],
              truncated: false,
            }),
          ),
        );
      },
    },
  },
];
