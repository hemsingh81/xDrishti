import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildExecStatus, buildOverview } from '@/test/executionFixtures';
import { pinClock } from '@/test/pinClock';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { ExecutionPage } from './components/ExecutionPage';
import { recordWrites } from './testing';

pinClock('2026-10-30T04:00:00Z');
recordWrites();

const open = (tab?: string) =>
  renderRoutes([{ path: '/execution', element: <ExecutionPage /> }], {
    initialPath: tab ? `/execution?tab=${tab}` : '/execution',
  });

beforeEach(() => {
  server.use(
    http.get('/api/execution/readiness', () => HttpResponse.json({ date: '2026-10-30', accounts: [] })),
    http.get('/api/execution/confirm-tickets', () => HttpResponse.json([])),
    http.get('/api/execution/day-unlocks', () => HttpResponse.json([])),
  );
});

describe('Execution page', () => {
  it('says Execution is off with the reasons, draws nothing as healthy, and the other tabs are empty without a failed request', async () => {
    server.use(
      http.get('/api/execution/status', () =>
        HttpResponse.json(
          buildExecStatus({
            accounts: [{ accountId: 'a1', mode: 'paper', gate: { open: false, reason: 'No mode ladder yet' } }],
          }),
        ),
      ),
    );
    open();
    const off = await screen.findByText('Execution is off');
    const box = off.closest<HTMLElement>('[role="status"]')!;
    expect(within(box).getByText(/Service: down/)).toBeInTheDocument();
    expect(within(box).getByText(/Environment: none/)).toBeInTheDocument();
    expect(within(box).getByText(/No mode ladder yet/)).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('has a labelled tablist, takes the tab from the address and changes it on click', async () => {
    server.use(
      http.get('/api/execution/blotter', () => HttpResponse.json([])),
      http.get('/api/execution/activity', () => HttpResponse.json([])),
    );
    const view = open('orders');
    expect(screen.getByRole('tablist', { name: 'Execution views' })).toBeInTheDocument();
    expect(await screen.findByText('No orders for this view.')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Orders' })).toHaveAttribute('aria-selected', 'true');
    await userEvent.click(screen.getByRole('tab', { name: 'Activity' }));
    expect(view.router.state.location.search).toBe('?tab=activity');
    expect(await screen.findByText('Nothing has happened for this view.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('tab', { name: 'Today' }));
    expect(view.router.state.location.search).toBe('');
  });

  it('falls back to Today for an unknown tab', async () => {
    open('nonsense');
    expect(screen.getByRole('tab', { name: 'Today' })).toHaveAttribute('aria-selected', 'true');
    await screen.findByText('Execution is off');
  });

  it('shows the evidence line from the server at the foot', async () => {
    server.use(
      http.get('/api/execution/overview', () =>
        HttpResponse.json(buildOverview({ limits: ['Costs are not included.'] })),
      ),
    );
    open();
    const foot = await screen.findByLabelText('Evidence');
    expect(await within(foot).findByText('Costs are not included.')).toBeInTheDocument();
  });

  it('shows an error with retry when a tab cannot be read', async () => {
    let calls = 0;
    server.use(
      http.get('/api/execution/activity', () => {
        calls += 1;
        return calls === 1 ? HttpResponse.json({ title: 'Activity is down' }, { status: 500 }) : HttpResponse.json([]);
      }),
    );
    open('activity');
    expect(await screen.findByRole('alert')).toHaveTextContent('Activity is down');
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByText('Nothing has happened for this view.')).toBeInTheDocument();
  });
});
