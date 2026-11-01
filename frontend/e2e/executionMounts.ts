import { http, HttpResponse } from 'msw';
import { buildLimitsView, buildModes } from '../src/test/executionFixtures';
import { server } from '../src/test/server';

/** The Execution page, Modes tab: one account; the switch and the drills answer from the default handlers. */
export function mountModes(): void {
  server.use(
    http.get('/api/execution/modes', () => HttpResponse.json(buildModes())),
    http.get('/api/execution/limits', () => HttpResponse.json(buildLimitsView())),
  );
}
