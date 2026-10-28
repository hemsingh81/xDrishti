import { screen, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Schemas } from '@/shared/api';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { LivePage } from './components/LivePage';

type View = Schemas['LivePhaseCheckView'];

const criterion = (code: string, verdict: string, caveat: string | null = null) => ({
  code,
  name: `Criterion ${code}`,
  verdict,
  detail: `Detail of ${code}`,
  caveat,
});

const view = (over: Partial<View['report']> = {}, state = 'value'): View => ({
  state,
  text: 'printable',
  report: {
    through: '2026-11-06',
    windowStart: '2026-10-12',
    elapsedDays: 26,
    sessions: 20,
    exitVerdict: 'MET',
    exitCriterion: 'Exit criterion MET (live only, 20 sessions)',
    criteria: [
      criterion('C1', 'MET'),
      criterion('C2', 'NOT MET', 'Slippage is modelled'),
      criterion('C3', 'NOT EVALUABLE'),
    ],
    weeks: [
      {
        weekStart: '2026-11-02',
        sessions: 5,
        tickets: 9,
        outcomes: {},
        roundTrips: 4,
        cumulativeRoundTrips: 12,
        step: 2,
        scale: null,
        captureComplete: 4,
        halts: 0,
        holds: 1,
        breaches: 0,
      },
    ],
    printed: [{ label: 'Net result', value: 'not computed' }],
    setup: [{ name: 'Live capture', on: false, kind: 'service', blocks: true }],
    blockers: ['Owner has not confirmed the exit'],
    recommendation: 'Hold at the current step.',
    limits: ['Records only.'],
    ...over,
  },
});

function mount(body: View | 'fail' | 'none') {
  server.use(
    http.get('/api/live-trading/phase-check', () =>
      body === 'fail'
        ? HttpResponse.json({ title: 'Broken', status: 500, code: 'x' }, { status: 500 })
        : body === 'none'
          ? HttpResponse.json({ title: 'No live account', status: 404, code: 'live.not_found' }, { status: 404 })
          : HttpResponse.json(body),
    ),
  );
  renderRoutes([{ path: '/live', element: <LivePage /> }], { initialPath: '/live?tab=phase-check' });
}

describe('the Phase check tab', () => {
  it('shows the headline with its caveat, the recommendation and each verdict as a word', async () => {
    mount(view());
    expect(
      await screen.findByRole('heading', { name: 'Exit criterion MET (live only, 20 sessions)' }),
    ).toBeInTheDocument();
    expect(screen.getByText('Hold at the current step.')).toBeInTheDocument();
    const criteria = screen.getByRole('region', { name: 'Criteria' });
    expect(within(criteria).getByText('MET')).toBeInTheDocument();
    expect(within(criteria).getByText('NOT MET')).toBeInTheDocument();
    expect(within(criteria).getByText('NOT EVALUABLE')).toBeInTheDocument();
    expect(within(criteria).getByText('Slippage is modelled')).toBeInTheDocument();
    expect(screen.getByRole('table', { name: 'Week lines' })).toBeInTheDocument();
    expect(screen.getByText('Net result: not computed')).toBeInTheDocument();
    expect(screen.getByText('Live capture: off')).toBeInTheDocument();
    expect(screen.getByText('Owner has not confirmed the exit')).toBeInTheDocument();
    expect(screen.getByText('Records only.')).toBeInTheDocument();
  });

  it('says there is nothing to check before a live session', async () => {
    mount(view({}, 'empty'));
    expect(await screen.findByText('No live session yet: nothing to check')).toBeInTheDocument();
  });

  it('says so without a live account', async () => {
    mount('none');
    expect(await screen.findByText(/No account is in manual-live mode/)).toBeInTheDocument();
  });

  it('shows an error state', async () => {
    mount('fail');
    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });

  it('holds no mutation', () => {
    const sources = import.meta.glob<string>(
      ['./components/Phase*.tsx', './api/phaseCheckQueries.ts', '!./**/*.test.tsx'],
      {
        query: '?raw',
        import: 'default',
        eager: true,
      },
    );
    expect(Object.keys(sources).length).toBe(4);
    for (const source of Object.values(sources)) expect(source).not.toMatch(/useMutation|api\.(POST|PUT|PATCH|DELETE)/);
  });
});
