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

const replayId = '22222222-2222-2222-2222-222222222222';
const replayLabels = [
  { code: 'settings_now', text: 'Settings are as of today.' },
  { code: 'costs_gross', text: 'before costs (charge sheet not filled, ISS-25)' },
];
const replayDone = { complete: true, sessionsDone: 2, sessionsTotal: 2, asOfSession: '2024-01-03' };

/** The SR-01 system replay screen: one finished run, or none. */
const mountReplay = (withRun: boolean) => () => {
  const head = { hindsight: replayLabels, costsLabel: replayLabels[1]?.text ?? '' };
  server.use(
    http.get('/api/baskets', () => HttpResponse.json([])),
    http.get('/api/risk-profiles', () => HttpResponse.json([])),
    http.get('/api/charge-sheet/confirmation', () => HttpResponse.json({}, { status: 404 })),
    http.get('/api/replay/runs', () =>
      HttpResponse.json(
        withRun
          ? [
              {
                ...head,
                id: replayId,
                pairId: null,
                createdAt: '2026-11-12T03:00:00Z',
                from: '2024-01-01',
                to: '2024-01-03',
                mode: 'frozen',
                state: 'completed',
                completeness: replayDone,
                finalEquity: 1_001_000,
                maxDrawdownPct: 0.1,
                trialNumber: 1,
              },
            ]
          : [],
      ),
    ),
    http.get('/api/replay/runs/:id', () =>
      HttpResponse.json({
        ...head,
        id: replayId,
        pairId: null,
        createdAt: '2026-11-12T03:00:00Z',
        finishedAt: '2026-11-12T03:05:00Z',
        from: '2024-01-01',
        to: '2024-01-03',
        mode: 'frozen',
        state: 'completed',
        failReason: null,
        completeness: replayDone,
        basis: 'gross',
        trialNumber: 1,
        inputHash: 'a',
        resultHash: 'b',
        parameters: {},
        settingsSnapshot: {},
        figures: null,
        contribution: [],
      }),
    ),
    http.get('/api/replay/runs/:id/equity', () =>
      HttpResponse.json({
        ...head,
        runId: replayId,
        completeness: replayDone,
        capital: 1_000_000,
        benchmarkSymbol: null,
        benchmarkReason: 'No benchmark index is stored.',
        cashComplete: true,
        points: [],
      }),
    ),
    http.get('/api/replay/runs/:id/sessions', () =>
      HttpResponse.json({ ...head, runId: replayId, completeness: replayDone, sessions: [] }),
    ),
    http.get('/api/replay/runs/:id/trades', () =>
      HttpResponse.json({ ...head, runId: replayId, completeness: replayDone, trades: [] }),
    ),
  );
};

/** The AU-15 simulation report screen: a running job with a partial report, and the state before any job. */
export const simulationScreens: Screen[] = [
  {
    id: 'AU-15-simulation',
    route: '/reports/simulation',
    mounts: { full: mountSimulation(true), empty: mountSimulation(false) },
  },
  {
    id: 'SR-01-system-replay',
    route: '/simulation/replay',
    mounts: { full: mountReplay(true), empty: mountReplay(false) },
  },
];
