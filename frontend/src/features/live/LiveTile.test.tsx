import { screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { pinClock } from '@/test/pinClock';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { LiveTile } from './components/LiveTile';

pinClock('2026-10-28T06:30:00Z');

const ramp = {
  accountId: 'a',
  step: 1,
  scaleEffective: 0.25,
  hold: true,
  holdReason: null,
  nextStep: 2,
  eligible: false,
  gatesBuilt: true,
  intendedRiskPct: 0.5,
  liveRiskPct: 0.125,
  writesEnabled: true,
  history: [],
  notice: 'x',
  gates: [],
  breaches: [],
  holdConditions: [],
  loss: {
    date: '2026-10-28',
    health: 'gap',
    state: 'unavailable',
    netRupees: null,
    lossR: null,
    limitR: 2,
    oneRupees: 125,
    carriedQty: 0,
    openUnmarked: 0,
    note: 'n',
  },
  halts: [
    {
      id: 1,
      accountId: 'a',
      kind: 'daily',
      periodStart: '2026-10-28',
      limitR: 2,
      limitPct: 1.5,
      measuredR: 2.2,
      measuredPct: 0.3,
      measureState: 'value',
      action: 'stop_new',
      openedAt: '2026-10-28T05:00:00Z',
    },
  ],
  slippageKind: null,
  autoRampUp: false,
  exitConfirmed: false,
  sessions: 0,
};

describe('the Today tile of the live phase', () => {
  it('shows the step, the scale, the hold, the halt in force and the capture health of a live account', async () => {
    server.use(http.get('/api/live-trading/ramp', () => HttpResponse.json(ramp)));
    renderRoutes([{ path: '/', element: <LiveTile /> }]);

    expect(await screen.findByRole('heading', { name: 'Live trading' })).toBeInTheDocument();
    expect(await screen.findByText(/25 % of the risk/)).toBeInTheDocument();
    expect(screen.getByText(/On hold\./)).toBeInTheDocument();
    expect(screen.getByText(/A daily loss halt is in force/)).toBeInTheDocument();
    expect(screen.getByText(/Capture gap\./)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open the ramp' })).toHaveAttribute('href', '/live?tab=ramp');
  });

  it('shows nothing when no account is in the live mode', async () => {
    server.use(http.get('/api/accounts', () => HttpResponse.json([])));
    renderRoutes([{ path: '/', element: <LiveTile /> }]);

    await new Promise((r) => setTimeout(r, 20));
    expect(screen.queryByText('Live trading')).not.toBeInTheDocument();
  });
});
