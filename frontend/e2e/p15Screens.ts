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
];
