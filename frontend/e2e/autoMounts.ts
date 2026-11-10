import './shim';
import { http, HttpResponse } from 'msw';
import { buildAuto, buildAutoSuggestions } from '../src/test/autoFixtures';
import type { Screen } from './matrix';
import { server } from '../src/test/server';

/** The AU-18 Auto screen: ON with a published night and suggestions, and the default OFF state with no plan. */
export const autoScreens: Screen[] = [
  {
    id: 'AU-18-auto',
    route: '/auto',
    mounts: {
      full: () => {
        server.use(
          http.get('/api/auto', () => HttpResponse.json(buildAuto({ on: true, complete: true }))),
          http.get('/api/auto/suggestions', () => HttpResponse.json(buildAutoSuggestions())),
        );
      },
      empty: () => {
        /* the defaults: Auto OFF, no plan */
      },
    },
  },
];
