import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { pollIntervalMs } from '@/features/execution';
import { captureIntervals } from '@/test/clock';
import { buildExecStatus } from '@/test/executionFixtures';
import { buildAccountDetail, buildConnected, summaryOf } from '@/test/fixtures';
import { pinClock } from '@/test/pinClock';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { NotificationBell } from './components/NotificationBell';

pinClock('2026-10-30T04:00:00Z');

const item = (over = {}) => ({
  id: 'exec:halt:3',
  kind: 'exec_halted',
  severity: 'error',
  text: 'Trading is halted (entries).',
  title: 'Halted',
  at: '2026-10-30T03:55:00Z',
  actions: [{ label: 'Open', kind: 'route', target: '/execution' }],
  since: '2026-10-30T03:55:00Z',
  resolved: false,
  accountId: null,
  ...over,
});

const renderBell = () => renderRoutes([{ path: '/', element: <NotificationBell storage={null} /> }]);
const serve = (items: unknown[], status = {}) => {
  server.use(
    http.get('/api/accounts', () =>
      HttpResponse.json([summaryOf(buildAccountDetail({ connection: buildConnected() }))]),
    ),
    http.get('/api/execution/notices', () => HttpResponse.json({ items })),
    http.get('/api/execution/status', () => HttpResponse.json(buildExecStatus(status))),
  );
};

describe('Notification bell: execution notices', () => {
  it('counts the open error, lists a resolved automatic action without counting it', async () => {
    serve([
      item(),
      item({
        id: 'exec:action:9',
        kind: 'exec_auto_action',
        severity: 'info',
        text: 'Placed BUY 120 TATAPOWER',
        resolved: true,
      }),
    ]);
    renderBell();
    await userEvent.click(await screen.findByRole('button', { name: 'Notifications, 1 unread' }));
    const drawer = await screen.findByRole('dialog', { name: 'Notifications' });
    expect(within(drawer).getByText(/Trading is halted/)).toBeInTheDocument();
    await userEvent.click(within(drawer).getByRole('button', { name: /Resolved/ }));
    expect(await within(drawer).findByText(/Placed BUY 120 TATAPOWER/)).toBeInTheDocument();
  });

  it('polls every 5 s while the server says so, else every 60 s', async () => {
    const timers = captureIntervals(5000, 60_000);
    serve([], { anyAutomated: true, marketOpen: true, pollSeconds: 5 });
    renderBell();
    await vi.waitFor(() => {
      expect(timers.count(5000)).toBeGreaterThan(0);
    });
  });

  it('chooses the interval from the server field, and 60 s for a hidden tab', () => {
    const fast = buildExecStatus({ pollSeconds: 5 });
    expect(pollIntervalMs(fast, false)).toBe(5000);
    expect(pollIntervalMs(fast, true)).toBe(60_000);
    expect(pollIntervalMs(undefined, false)).toBe(60_000);
    expect(pollIntervalMs(buildExecStatus({ pollSeconds: 60 }), false)).toBe(60_000);
  });
});
