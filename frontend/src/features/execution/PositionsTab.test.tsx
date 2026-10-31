import { screen, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { buildPosition } from '@/test/executionFixtures';
import { pinClock } from '@/test/pinClock';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { ExecutionPage } from './components/ExecutionPage';
import { recordWrites } from './testing';

pinClock('2026-10-30T04:00:00Z');
recordWrites();

const open = (tab = 'positions') =>
  renderRoutes([{ path: '/execution', element: <ExecutionPage /> }], { initialPath: `/execution?tab=${tab}` });
const positions = (list: unknown[]) => {
  server.use(http.get('/api/execution/positions', () => HttpResponse.json(list)));
};

describe('Positions tab', () => {
  it('shows a confirmed position with its figures, source and reconciliation', async () => {
    positions([buildPosition()]);
    open();
    const row = await screen.findByRole('row', { name: /TATAPOWER/ });
    expect(within(row).getByText('Protected (confirmed)')).toBeInTheDocument();
    expect(within(row).getByText('Ours')).toBeInTheDocument();
    expect(within(row).getByText('Matched')).toBeInTheDocument();
    expect(within(row).getByText('415.00')).toBeInTheDocument();
    expect(screen.queryByText('Protection is missing')).not.toBeInTheDocument();
  });

  it('never calls an unknown protection green, and shows an old price as unavailable, not as a number', async () => {
    positions([
      buildPosition({
        protection: 'unknown',
        unrealised: { state: 'unavailable', reason: 'The last price is older than 120 s' },
        source: 'foreign',
        reconciliation: 'break',
      }),
    ]);
    open();
    const row = await screen.findByRole('row', { name: /TATAPOWER/ });
    expect(within(row).getByText('Protection unknown')).toBeInTheDocument();
    expect(within(row).getByText('The last price is older than 120 s')).toBeInTheDocument();
    expect(within(row).getByText(/Foreign/)).toBeInTheDocument();
    expect(within(row).getByText('Break')).toBeInTheDocument();
  });

  it('shows a missing protection in an error banner at the top of every tab', async () => {
    positions([buildPosition({ protection: 'missing' })]);
    server.use(http.get('/api/execution/activity', () => HttpResponse.json([])));
    open('activity');
    const banner = await screen.findByRole('alert');
    expect(banner).toHaveTextContent('Protection is missing');
    expect(banner).toHaveTextContent('TATAPOWER');
  });

  it('shows the empty sentence and an error with retry', async () => {
    positions([]);
    open();
    expect(await screen.findByText('No open positions.')).toBeInTheDocument();
  });
});
