import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { fakeTimers, pinClock } from '@/test/pinClock';
import { stubPhoneWidth } from '@/test/matchMedia';
import { renderRoutes } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import type { RecoveryRow, RecoveryStatus } from '@/shared/api/types';
import { RecoveryPage } from './components/RecoveryPage';

vi.mock('./lib/polling', () => ({ servicesRunningPollMs: 25, servicesIdlePollMs: 60_000, recoveryPollMs: 25 }));

pinClock('2026-11-09T10:00:00Z');

const row = (over: Partial<RecoveryRow> = {}): RecoveryRow => ({
  serviceId: 'eod',
  serviceName: 'End-of-day data run',
  status: 'missed',
  reason: 'The computer was off.',
  attempts: 0,
  from: '2026-11-05',
  to: '2026-11-05',
  runId: null,
  canRetry: false,
  retryKind: null,
  missingSessions: [],
  ...over,
});

const status = (over: Partial<RecoveryStatus> = {}): RecoveryStatus => ({
  checkedAt: '2026-11-09T09:55:00Z',
  running: false,
  latestPass: null,
  days: 14,
  counts: { missed: 0, recovered: 0, stillFailing: 0 },
  truncated: false,
  planUnavailable: null,
  sessions: [],
  ...over,
});

const withSession = (rows: RecoveryRow[], over: Partial<RecoveryStatus> = {}) =>
  status({
    counts: { missed: 1, recovered: 1, stillFailing: 1 },
    sessions: [{ session: '2026-11-05', items: rows }],
    ...over,
  });

function mountRead(body: () => RecoveryStatus) {
  let reads = 0;
  server.use(
    http.get('/api/services/recovery', () => {
      reads += 1;
      return HttpResponse.json(body());
    }),
  );
  return () => reads;
}

const renderPage = () =>
  renderRoutes(
    [
      { path: '/services/recovery', element: <RecoveryPage /> },
      { path: '/services', element: <p>Services screen</p> },
    ],
    { initialPath: '/services/recovery' },
  );

describe('Recovery screen', () => {
  it('shows a loading state, then the empty state with when it was last checked', async () => {
    mountRead(() => status());
    renderPage();
    expect(screen.getByRole('status')).toBeInTheDocument();
    expect(await screen.findByText(/Nothing was missed in the last 14 days/)).toBeInTheDocument();
    expect(screen.getAllByText(/Last checked/).length).toBeGreaterThan(0);
  });

  it('shows an error with Retry that reads again', async () => {
    let fail = true;
    server.use(
      http.get('/api/services/recovery', () =>
        fail ? problemResponse({ title: 'Boom', status: 500 }, 500) : HttpResponse.json(status()),
      ),
    );
    renderPage();
    const retry = await screen.findByRole('button', { name: 'Retry' });
    fail = false;
    await userEvent.click(retry);
    expect(await screen.findByText(/Nothing was missed/)).toBeInTheDocument();
  });

  it('leads with three counts, then what needs attention; partial lists dates and is not called recovered', async () => {
    mountRead(() =>
      withSession([
        row({ status: 'partial', missingSessions: ['2026-11-04', '2026-11-05'], reason: 'Some days failed.' }),
        row({ serviceId: 'q', serviceName: 'Quality check', status: 'recovered', reason: null }),
        row({ serviceId: 'x', serviceName: 'Odd job', status: 'brand_new', reason: null }),
      ]),
    );
    renderPage();
    expect(await screen.findByText('Needs attention')).toBeInTheDocument();
    expect(screen.getAllByRole('term').map((t) => t.textContent)).toEqual(['Missed', 'Recovered', 'Still failing']);
    const attention = screen.getByRole('region', { name: 'Needs attention' });
    expect(within(attention).getByText(/Partly recovered/)).toBeInTheDocument();
    expect(within(attention).queryByText(/— Recovered/)).not.toBeInTheDocument();
    expect(within(attention).getByText(/Still missing: .*4.*5/)).toBeInTheDocument();
    expect(within(attention).getByText(/Unavailable/)).toBeInTheDocument();
    expect(within(attention).queryByText('Quality check')).not.toBeInTheDocument();
  });

  it('says a not recoverable row cannot be recovered and offers no retry; says when only the newest are shown', async () => {
    mountRead(() =>
      withSession([row({ status: 'not_recoverable', reason: 'The source keeps no history.' })], { truncated: true }),
    );
    renderPage();
    expect(await screen.findByText('The source keeps no history.')).toBeInTheDocument();
    expect(screen.getByText(/newest 500/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Retry recovery' })).not.toBeInTheDocument();
  });

  it('polls only while a pass runs and stops when it ends', async () => {
    let running = true;
    const reads = mountRead(() => withSession([row()], { running }));
    fakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'], shouldAdvanceTime: true });
    renderPage();
    await screen.findByText(/A recovery pass is running now/);
    await vi.advanceTimersByTimeAsync(100);
    await waitFor(() => {
      expect(reads()).toBeGreaterThan(2);
    });
    running = false;
    await vi.advanceTimersByTimeAsync(100);
    await waitFor(() => {
      expect(screen.queryByText(/A recovery pass is running now/)).not.toBeInTheDocument();
    });
    const settled = reads();
    await vi.advanceTimersByTimeAsync(2000);
    expect(reads()).toBe(settled);
  });

  it('does not poll when nothing runs', async () => {
    const reads = mountRead(() => status());
    fakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'], shouldAdvanceTime: true });
    renderPage();
    await screen.findByText(/Nothing was missed/);
    await vi.advanceTimersByTimeAsync(2000);
    expect(reads()).toBe(1);
  });

  it('shows why the plan is unavailable instead of "nothing was missed"', async () => {
    mountRead(() => status({ planUnavailable: { reason: 'The calendar could not be read.' } }));
    renderPage();
    expect(await screen.findByText('The calendar could not be read.')).toBeInTheDocument();
    expect(screen.queryByText(/Nothing was missed/)).not.toBeInTheDocument();
  });

  it('labels a partial row’s retry as a re-check and says a spent failing row will not run again', async () => {
    mountRead(() =>
      withSession([
        row({ status: 'partial', canRetry: true, retryKind: 'recheck', missingSessions: ['2026-11-04'] }),
        row({ serviceId: 'f', serviceName: 'Fails', status: 'still_failing', attempts: 3 }),
      ]),
    );
    renderPage();
    expect(await screen.findByText(/re-checks the data already stored/)).toBeInTheDocument();
    expect(screen.getByText(/Tried 3 times/)).toBeInTheDocument();
  });

  it('is usable on a phone: sessions are tables, counts and Retry are present', async () => {
    stubPhoneWidth();
    mountRead(() => withSession([row({ canRetry: true, retryKind: 'run' })]));
    renderPage();
    expect(await screen.findByRole('table', { name: /Session/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry recovery' })).toBeInTheDocument();
    expect(screen.getAllByRole('term')).toHaveLength(3);
  });

  it('offers one confirmed Retry that starts a pass, and starts nothing on load', async () => {
    const posts: string[] = [];
    mountRead(() =>
      withSession([
        row({ canRetry: true, retryKind: 'run' }),
        row({ serviceId: 'b', serviceName: 'B', canRetry: true, retryKind: 'run' }),
      ]),
    );
    server.use(
      http.post('/api/services/recovery/retry', () => {
        posts.push('x');
        return HttpResponse.json({ requestedAt: '2026-11-09T10:00:00Z', started: true, steps: 2 }, { status: 202 });
      }),
    );
    renderPage();
    const button = await screen.findByRole('button', { name: 'Retry recovery' });
    expect(screen.getAllByRole('button', { name: 'Retry recovery' })).toHaveLength(1);
    expect(posts).toHaveLength(0);
    await userEvent.click(button);
    const dialog = await screen.findByRole('dialog', { name: 'Retry recovery?' });
    expect(posts).toHaveLength(0);
    await userEvent.click(within(dialog).getByRole('button', { name: 'Start recovery' }));
    await waitFor(() => {
      expect(posts).toHaveLength(1);
    });
    expect(await screen.findByText('Recovery started.')).toBeInTheDocument();
  });

  it('shows the server’s words on 409', async () => {
    mountRead(() => withSession([row({ canRetry: true, retryKind: 'run' })]));
    server.use(
      http.post('/api/services/recovery/retry', () =>
        problemResponse(
          { title: 'A recovery pass is already running.', status: 409, code: 'recovery.already_running' },
          409,
        ),
      ),
    );
    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: 'Retry recovery' }));
    const dialog = await screen.findByRole('dialog');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Start recovery' }));
    expect(await screen.findByText('A recovery pass is already running.')).toBeInTheDocument();
  });

  it('says when nothing ran, only partial items were re-checked', async () => {
    mountRead(() => withSession([row({ status: 'partial', canRetry: true, retryKind: 'recheck' })]));
    server.use(
      http.post('/api/services/recovery/retry', () =>
        HttpResponse.json({ requestedAt: '2026-11-09T10:00:00Z', started: false, steps: 0 }, { status: 202 }),
      ),
    );
    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: 'Retry recovery' }));
    const dialog = await screen.findByRole('dialog');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Start recovery' }));
    expect(await screen.findByText('Nothing to run; re-checked what was partial.')).toBeInTheDocument();
  });

  it('shows recovery.not_recorded in the server’s words', async () => {
    mountRead(() => withSession([row({ canRetry: true, retryKind: 'run' })]));
    server.use(
      http.post('/api/services/recovery/retry', () =>
        problemResponse({ title: 'Recovery is not being recorded.', status: 409, code: 'recovery.not_recorded' }, 409),
      ),
    );
    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: 'Retry recovery' }));
    const dialog = await screen.findByRole('dialog');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Start recovery' }));
    expect(await screen.findByText('Recovery is not being recorded.')).toBeInTheDocument();
  });
});
