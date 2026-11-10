import './shim';
import { http, HttpResponse } from 'msw';
import { buildEquity, buildEvidence, buildGroup } from '../src/test/reportFixtures';
import type { Screen } from './matrix';
import { server } from '../src/test/server';

const simJobId = '11111111-1111-1111-1111-111111111111';
/** The simulation report screen (AU-15): a running job with a partial report, or no job yet. */
const mountSimulation = (withJob: boolean) => () => {
  server.use(
    http.get('/api/baskets', () => HttpResponse.json([])),
    http.get('/api/simulate-all/current', () =>
      withJob
        ? HttpResponse.json({
            id: simJobId,
            state: 'running',
            pauseReason: null,
            windowFrom: '2018-01-01',
            windowTo: '2025-12-31',
            holdOutStart: '2026-01-01',
            coveredFrom: '2018-01-01',
            coveredTo: '2022-01-01',
            chunksTotal: 100,
            chunksDone: 30,
            chunksFailed: 0,
            chunksRunning: 2,
            chunksRemaining: 68,
            workers: 2,
            startedBy: 'owner',
            startedAt: '2026-11-10T01:00:00Z',
            updatedAt: '2026-11-10T03:59:00Z',
            finishedAt: null,
            estimate: null,
          })
        : HttpResponse.json(
            { title: 'No job.', status: 404, code: 'simulate_all.no_active_job' },
            { status: 404, headers: { 'content-type': 'application/problem+json' } },
          ),
    ),
    http.get('/api/reports/summary', () =>
      HttpResponse.json({
        all: buildGroup(),
        evidence: buildEvidence({
          completeness: {
            kind: 'training_simulation',
            label: 'Training simulation, not evidence.',
            simJob: simJobId,
            jobState: 'running',
            complete: false,
            chunksDone: 30,
            chunksTotal: 100,
            chunksFailed: 0,
            chunksRunning: 2,
            chunksPending: 68,
            asOf: '2026-11-10T03:59:00Z',
            windowFrom: '2018-01-01',
            windowTo: '2025-12-31',
            holdOutStart: '2026-01-01',
            rangeFrom: '2018-01-01',
            rangeTo: '2025-12-31',
            rangeCut: false,
            locked: null,
            symbolsWithoutResults: [],
            trainingRecord: `/api/simulate-all/${simJobId}/training`,
          },
        }),
      }),
    ),
    http.get('/api/reports/equity', () => HttpResponse.json(buildEquity())),
  );
};

/** The AU-15 simulation report screen: a running job with a partial report, and the state before any job. */
export const simulationScreens: Screen[] = [
  {
    id: 'AU-15-simulation',
    route: '/reports/simulation',
    mounts: { full: mountSimulation(true), empty: mountSimulation(false) },
  },
];
