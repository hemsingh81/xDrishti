import { http, HttpResponse } from 'msw';
import { server } from '../src/test/server';
import type { Screen } from './matrix';

/** The AU-04 recovery screen: a partial row with a re-check, and the empty state. */
export const recoveryScreens: Screen[] = [
  {
    id: 'AU-04-recovery',
    route: '/services/recovery',
    mounts: {
      full: () => {
        server.use(
          http.get('/api/services/recovery', () =>
            HttpResponse.json({
              checkedAt: '2026-11-09T09:55:00Z',
              running: false,
              latestPass: null,
              days: 14,
              counts: { missed: 1, recovered: 1, stillFailing: 0 },
              truncated: false,
              planUnavailable: null,
              sessions: [
                {
                  session: '2026-11-05',
                  items: [
                    {
                      serviceId: 'eod',
                      serviceName: 'End-of-day data run',
                      status: 'partial',
                      reason: 'Some days failed.',
                      attempts: 1,
                      from: '2026-11-04',
                      to: '2026-11-05',
                      runId: null,
                      canRetry: true,
                      retryKind: 'recheck',
                      missingSessions: ['2026-11-04'],
                    },
                  ],
                },
              ],
            }),
          ),
        );
      },
      empty: () => {
        server.use(
          http.get('/api/services/recovery', () =>
            HttpResponse.json({
              checkedAt: '2026-11-09T09:55:00Z',
              running: false,
              latestPass: null,
              days: 14,
              counts: { missed: 0, recovered: 0, stillFailing: 0 },
              truncated: false,
              planUnavailable: null,
              sessions: [],
            }),
          ),
        );
      },
    },
  },
];
