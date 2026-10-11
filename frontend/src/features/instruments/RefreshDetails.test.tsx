import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import {
  buildProblem,
  buildRefreshRun,
  buildRefreshStatus,
  buildRunningRun,
  sampleInstruments,
  searchInstruments,
} from '@/test/fixtures';
import { problemResponse, server } from '@/test/server';
import { renderInstruments } from './testing';

// Poll fast so a whole refresh fits in a test.
vi.mock('./lib/polling', () => ({ refreshPollMs: 25, startGraceMs: 20_000 }));

const hourAgo = () => new Date(Date.now() - 3_600_000).toISOString();
const statusOf = (status: ReturnType<typeof buildRefreshStatus>) =>
  http.get('/api/instruments/refresh/status', () => HttpResponse.json(status));
const lastLine = () => screen.findByRole('button', { name: /^Last (refreshed|attempt)/ });
/** Opens the details from the "last refreshed" line in the header. */
async function openDetails() {
  await userEvent.click(await lastLine());
  return screen.findByRole('region', { name: 'Master list' });
}
/** Lets `ms` of (fake) time pass; the tests that call it run on fake timers that also follow the real clock. */
const settle = (ms: number) => vi.advanceTimersByTimeAsync(ms);
const fakeTimers = () => {
  vi.useFakeTimers({ shouldAdvanceTime: true });
};

afterEach(() => {
  vi.useRealTimers();
});

describe('Refresh header: what is always visible', () => {
  it('shows when the list was last refreshed and how many are active, and keeps the details hidden', async () => {
    const finishedAt = hourAgo();
    server.use(statusOf(buildRefreshStatus({ last: buildRefreshRun({ finishedAt }) })));
    renderInstruments();

    const line = await lastLine();
    expect(line).toHaveTextContent('Last refreshed 1 hour ago · 4,381 active');
    expect(within(line).getByText('1 hour ago')).toHaveAttribute('datetime', finishedAt);
    expect(line).toHaveAttribute('aria-expanded', 'false');
    expect(screen.getByRole('button', { name: 'Refresh master list' })).toBeEnabled();
    expect(screen.queryByRole('region', { name: 'Master list' })).not.toBeInTheDocument();
    expect(screen.queryByText('+12 added · 3 updated · 5 retired · 4,381 active')).not.toBeInTheDocument();
    expect(screen.queryByText(/Daily at 08:45 IST/)).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Last refresh (failed|has warnings)|Refreshing/ }),
    ).not.toBeInTheDocument();
  });

  it('never opens the details by itself, not even for a failed run', async () => {
    fakeTimers();
    server.use(statusOf(buildRefreshStatus({ last: buildRefreshRun({ status: 'failed', error: 'Dhan is down.' }) })));
    renderInstruments();

    await screen.findByRole('button', { name: 'Last refresh failed' });
    await settle(60);
    expect(screen.queryByRole('region', { name: 'Master list' })).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('shows a chip for a failed last refresh, which opens the reason', async () => {
    server.use(
      statusOf(
        buildRefreshStatus({ last: buildRefreshRun({ status: 'failed', error: 'Dhan did not answer (HTTP 503).' }) }),
      ),
    );
    renderInstruments();
    expect(await lastLine()).toHaveTextContent(/^Last attempt/);

    await userEvent.click(await screen.findByRole('button', { name: 'Last refresh failed' }));

    const panel = await screen.findByRole('region', { name: 'Master list' });
    expect(within(panel).getByRole('alert')).toHaveTextContent(
      'The refresh failed: Dhan did not answer (HTTP 503). The previous list is unchanged.',
    );
    expect(within(panel).getByText('Failed')).toBeInTheDocument();
  });

  it('shows a chip when the last refresh had warnings, and lists them on request', async () => {
    server.use(
      statusOf(
        buildRefreshStatus({
          last: buildRefreshRun({
            warnings: ['The NSE sector list could not be read; sectors were not updated.', '2 symbols collide.'],
          }),
        }),
      ),
    );
    renderInstruments();

    await userEvent.click(await screen.findByRole('button', { name: 'Last refresh has warnings' }));

    const warnings = await screen.findByText('2 warnings');
    const box = warnings.closest('[role="alert"]');
    expect(box).toHaveTextContent('The NSE sector list could not be read; sectors were not updated.');
    expect(box).toHaveTextContent('2 symbols collide.');
  });

  it('shows a Refreshing chip for a run that is already in progress, without opening the details', async () => {
    server.use(statusOf(buildRefreshStatus({ current: buildRunningRun(), totalActive: 4_000 })));
    renderInstruments();

    const chip = await screen.findByRole('button', { name: 'Refreshing…' });
    expect(screen.queryByRole('region', { name: 'Master list' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Refresh master list' })).toHaveAttribute('aria-disabled', 'true');
    expect(screen.getByRole('button', { name: 'More refresh options' })).toHaveAttribute('aria-disabled', 'true');

    await userEvent.click(chip);

    expect(await screen.findByText('Manual refresh in progress — Reading the file')).toBeInTheDocument();
    expect(screen.getByText('1,20,000 rows read')).toBeInTheDocument();
    expect(screen.getByRole('progressbar', { name: 'Refresh progress' })).toBeInTheDocument();
  });

  it('shows an error with a retry in the header when the status cannot be read', async () => {
    let fail = true;
    server.use(
      http.get('/api/instruments/refresh/status', () =>
        fail
          ? HttpResponse.json({ title: 'Status is unavailable' }, { status: 503 })
          : HttpResponse.json(buildRefreshStatus()),
      ),
    );
    renderInstruments();

    expect(await screen.findByText('Status is unavailable')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Refresh master list' })).toBeDisabled();
    fail = false;
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));

    expect(await lastLine()).toHaveTextContent('Last refreshed');
  });
});

describe('Refresh details: opened from the header', () => {
  it('shows the result (relative and exact time), the summary and the schedule, and can be dismissed', async () => {
    renderInstruments();

    const panel = await openDetails();

    expect(within(panel).getByText('+12 added · 3 updated · 5 retired · 4,381 active')).toBeInTheDocument();
    expect(within(panel).getByText('Updated')).toBeInTheDocument();
    expect(within(panel).getByText(/Manual refresh/)).toBeInTheDocument();
    expect(within(panel).getByText(/Daily at 08:45 IST · next run \d\d:\d\d, \d+ \w+ IST/)).toBeInTheDocument();
    expect(await lastLine()).toHaveAttribute('aria-expanded', 'true');

    await userEvent.click(within(panel).getByRole('button', { name: 'Close refresh details' }));

    expect(screen.queryByRole('region', { name: 'Master list' })).not.toBeInTheDocument();
    expect(await lastLine()).toHaveAttribute('aria-expanded', 'false');
    expect(screen.getByRole('button', { name: 'Refresh master list' })).toBeEnabled();
  });

  it('leaves out the zero counts', async () => {
    server.use(
      statusOf(buildRefreshStatus({ last: buildRefreshRun({ added: 0, updated: 0, retired: 2, revived: 1 }) })),
    );
    renderInstruments();
    await openDetails();

    expect(await screen.findByText('2 retired · 1 revived · 4,381 active')).toBeInTheDocument();
  });

  it('says plainly when the file was unchanged', async () => {
    server.use(
      statusOf(
        buildRefreshStatus({
          last: buildRefreshRun({ status: 'unchanged', trigger: 'daily', added: 0, updated: 0, retired: 0 }),
        }),
      ),
    );
    renderInstruments();
    await openDetails();

    expect(await screen.findByText('No changes — the file is unchanged')).toBeInTheDocument();
    expect(screen.getByText('Unchanged')).toBeInTheDocument();
    expect(screen.getByText(/Automatic daily refresh/)).toBeInTheDocument();
  });

  it('shows the full counts of the last run on demand', async () => {
    renderInstruments();
    await openDetails();
    const toggle = await screen.findByRole('button', { name: 'Show details' });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');

    await userEvent.click(toggle);

    expect(screen.getByRole('button', { name: 'Hide details' })).toHaveAttribute('aria-expanded', 'true');
    expect(await screen.findByText('Share 3,204 · ETF 405 · Index 119 · Future 653')).toBeInTheDocument();
    expect(screen.getByText('2,07,560')).toBeInTheDocument();
  });

  it('says when the automatic refresh is off', async () => {
    server.use(statusOf(buildRefreshStatus({ autoRefresh: { enabled: false, timeIst: '08:45', nextRunAt: null } })));
    renderInstruments();
    await openDetails();

    expect(await screen.findByText('Automatic refresh is off')).toBeInTheDocument();
  });
});

/** A fake backend for one refresh: running for `polls` status reads, then finished with `finish`. */
function fakeRefresh(finish: ReturnType<typeof buildRefreshRun>, polls = 2) {
  const state = { started: false, reads: 0, searches: 0, bodies: [] as unknown[], readsAfterDone: 0, done: false };
  const before = buildRefreshRun({ id: 'old-run', finishedAt: hourAgo() });
  server.use(
    http.post('/api/instruments/refresh', async ({ request }) => {
      state.bodies.push(await request.json());
      state.started = true;
      return HttpResponse.json({ refreshId: finish.id }, { status: 202 });
    }),
    http.get('/api/instruments/refresh/status', () => {
      if (!state.started) return HttpResponse.json(buildRefreshStatus({ last: before }));
      state.reads += 1;
      if (state.reads <= polls) {
        const phase = state.reads === 1 ? 'downloading' : 'saving';
        return HttpResponse.json(
          buildRefreshStatus({
            current: buildRunningRun({ id: finish.id, phase, rowsRead: state.reads * 5_000 }),
            last: before,
          }),
        );
      }
      if (state.done) state.readsAfterDone += 1;
      state.done = true;
      return HttpResponse.json(buildRefreshStatus({ last: finish }));
    }),
    http.get('/api/instruments', ({ request }) => {
      state.searches += 1;
      return HttpResponse.json(searchInstruments(sampleInstruments, new URL(request.url).searchParams));
    }),
  );
  return state;
}

describe('Refresh details: starting a refresh', () => {
  it('opens the details when Refresh is pressed: live progress, then the result, a toast and fresh lists; then it stops polling', async () => {
    const finish = buildRefreshRun({ id: 'run-1', added: 1, updated: 0, retired: 0, totalActive: 4_382 });
    fakeTimers();
    const state = fakeRefresh(finish, 3);
    renderInstruments();
    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });
    expect(screen.queryByRole('region', { name: 'Master list' })).not.toBeInTheDocument();
    const searchesBefore = state.searches;

    await userEvent.click(screen.getByRole('button', { name: 'Refresh master list' }));

    expect(await screen.findByRole('region', { name: 'Master list' })).toBeInTheDocument();
    expect(await screen.findByText(/Manual refresh in progress — Downloading the file from Dhan/)).toBeInTheDocument();
    expect(await screen.findByText(/Saving the list/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Refresh master list' })).toHaveAttribute('aria-disabled', 'true');
    expect(screen.getByRole('button', { name: 'Refreshing…' })).toBeInTheDocument(); // the chip

    const summary = '+1 added · 4,382 active';
    expect(await screen.findByText(summary)).toBeInTheDocument(); // the result stays in the open details
    expect(await screen.findByText(`Instrument list refreshed: ${summary}`)).toBeInTheDocument(); // toast
    expect(screen.getByRole('button', { name: 'Refresh master list' })).not.toHaveAttribute('aria-disabled');
    expect(screen.queryByRole('button', { name: 'Refreshing…' })).not.toBeInTheDocument();
    expect(state.bodies).toEqual([{ force: false }]);
    await vi.waitFor(() => {
      expect(state.searches).toBeGreaterThan(searchesBefore); // the list was reloaded
    });

    const reads = state.reads;
    await settle(250);
    expect(state.reads).toBe(reads); // no more polling once the run is over

    await userEvent.click(screen.getByRole('button', { name: 'Close refresh details' }));
    expect(screen.queryByRole('region', { name: 'Master list' })).not.toBeInTheDocument();
    expect(await lastLine()).toHaveTextContent('Last refreshed');
  });

  it('forces a full refresh from the menu', async () => {
    const state = fakeRefresh(buildRefreshRun({ id: 'run-2', trigger: 'forced', added: 0, updated: 0, retired: 0 }), 1);
    renderInstruments();

    await userEvent.click(await screen.findByRole('button', { name: 'More refresh options' }));
    await userEvent.click(screen.getByRole('menuitem', { name: /Force full refresh/ }));

    expect(await screen.findByText('Instrument list refreshed: No changes · 4,381 active')).toBeInTheDocument();
    expect(state.bodies).toEqual([{ force: true }]);
    expect(screen.getByText(/Forced full refresh/)).toBeInTheDocument();
  });

  it('tells the owner when an unchanged file needed no work', async () => {
    fakeRefresh(
      buildRefreshRun({ id: 'run-3', status: 'unchanged', trigger: 'manual', added: 0, updated: 0, retired: 0 }),
      0,
    );
    renderInstruments();

    await userEvent.click(await screen.findByRole('button', { name: 'Refresh master list' }));

    expect(await screen.findByText('No changes — the file is unchanged.')).toBeInTheDocument();
  });

  it('reports a failed run as an error toast and a failure chip', async () => {
    fakeRefresh(buildRefreshRun({ id: 'run-4', status: 'failed', error: 'Dhan did not answer.' }), 1);
    renderInstruments();

    await userEvent.click(await screen.findByRole('button', { name: 'Refresh master list' }));

    expect(await screen.findByText('The refresh failed — the previous list is unchanged.')).toBeInTheDocument();
    expect(screen.getByText(/Dhan did not answer\./)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Last refresh failed' })).toBeInTheDocument();
  });

  it('treats "already running" (409) as a cue to watch the running refresh, not as an error', async () => {
    let reads = 0;
    server.use(
      http.post('/api/instruments/refresh', () =>
        problemResponse(
          buildProblem('instruments.refresh_running', 409, { title: 'A refresh is already running.' }),
          409,
        ),
      ),
      http.get('/api/instruments/refresh/status', () => {
        reads += 1;
        // The first read is the page load; after the click somebody else's run shows up.
        return HttpResponse.json(
          reads === 1 ? buildRefreshStatus() : buildRefreshStatus({ current: buildRunningRun({ trigger: 'daily' }) }),
        );
      }),
    );
    renderInstruments();

    await userEvent.click(await screen.findByRole('button', { name: 'Refresh master list' }));

    expect(await screen.findByText(/Automatic daily refresh in progress/)).toBeInTheDocument();
    expect(screen.queryByText('A refresh is already running.')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Refresh master list' })).toHaveAttribute('aria-disabled', 'true');
  });

  it('shows why a refresh could not be started', async () => {
    server.use(
      http.post('/api/instruments/refresh', () =>
        HttpResponse.json({ title: 'Refresh is unavailable' }, { status: 503 }),
      ),
    );
    renderInstruments();

    await userEvent.click(await screen.findByRole('button', { name: 'Refresh master list' }));

    const alert = await within(await screen.findByRole('region', { name: 'Master list' })).findByRole('alert');
    expect(alert).toHaveTextContent('Refresh is unavailable');
    expect(screen.getByRole('button', { name: 'Refresh master list' })).not.toHaveAttribute('aria-disabled');
  });
});

describe('Refresh details: focus', () => {
  const panel = () => screen.findByRole('region', { name: 'Master list' });
  const closeButton = () => screen.getByRole('button', { name: 'Close refresh details' });

  it('moves focus into the panel when Refresh opens it, and back to the Refresh button when it is closed', async () => {
    fakeRefresh(buildRefreshRun({ id: 'run-f1' }), 0);
    renderInstruments();

    await userEvent.click(await screen.findByRole('button', { name: 'Refresh master list' }));

    expect(await panel()).toHaveFocus();
    await userEvent.click(closeButton());
    expect(screen.queryByRole('region', { name: 'Master list' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Refresh master list' })).toHaveFocus();
  });

  it('keeps focus out of <body> while the refresh runs: the busy button stays focusable', async () => {
    fakeRefresh(buildRefreshRun({ id: 'run-f2' }), 40);
    renderInstruments();

    await userEvent.click(await screen.findByRole('button', { name: 'Refresh master list' }));
    expect(await panel()).toHaveFocus();
    await screen.findByText(/Manual refresh in progress/);
    expect(document.activeElement).not.toBe(document.body);

    await userEvent.click(closeButton());

    const button = screen.getByRole('button', { name: 'Refresh master list' });
    expect(button).toHaveAttribute('aria-disabled', 'true');
    expect(button).toHaveFocus();
    await userEvent.click(button); // busy: nothing happens, no second run
    expect(screen.queryByRole('region', { name: 'Master list' })).not.toBeInTheDocument();
  });

  it('returns focus to the last-refreshed line that opened the panel', async () => {
    renderInstruments();

    const line = await lastLine();
    await userEvent.click(line);
    expect(await panel()).toHaveFocus();

    await userEvent.click(closeButton());
    expect(await lastLine()).toHaveFocus();
  });

  it('returns focus to the chip that opened the panel', async () => {
    server.use(statusOf(buildRefreshStatus({ last: buildRefreshRun({ status: 'failed', error: 'Dhan is down.' }) })));
    renderInstruments();

    await userEvent.click(await screen.findByRole('button', { name: 'Last refresh failed' }));
    expect(await panel()).toHaveFocus();

    await userEvent.click(closeButton());
    expect(screen.getByRole('button', { name: 'Last refresh failed' })).toHaveFocus();
  });

  it('opens the panel with focus from the "Force full refresh" menu too, and closing returns to the Refresh button', async () => {
    fakeRefresh(buildRefreshRun({ id: 'run-f3', trigger: 'forced' }), 0);
    renderInstruments();

    await userEvent.click(await screen.findByRole('button', { name: 'More refresh options' }));
    await userEvent.click(screen.getByRole('menuitem', { name: /Force full refresh/ }));

    expect(await panel()).toHaveFocus();
    await userEvent.click(closeButton());
    expect(screen.getByRole('button', { name: 'Refresh master list' })).toHaveFocus();
  });
});
