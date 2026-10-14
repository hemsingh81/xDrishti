import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { captureIntervals } from '@/test/clock';
import {
  brokerRunIds,
  buildBrokerCounts,
  buildBrokerKind,
  buildBrokerRun,
  buildBrokerStatus,
  buildNeverReadStatus,
  buildRunningStatus,
} from '@/test/brokerHistoryFixtures';
import {
  buildAccountDetail,
  buildConnection,
  ids,
  searchHoldings,
  sampleHoldings,
  summaryOf,
  withConnection,
} from '@/test/fixtures';
import { problemResponse, server } from '@/test/server';
import { brokerHistoryPollMs } from './api/brokerHistoryQueries';
import { renderPortfolio } from './testing';

const statusPath = '/api/accounts/:id/sync/broker-history/status';
const syncPath = '/api/accounts/:id/sync/broker-history';

const region = async () => within(await screen.findByRole('region', { name: 'Trades and ledger from Dhan' }));
const syncButton = () => screen.findByRole('button', { name: 'Sync trades & ledger' });

describe('Trades and ledger sync line', () => {
  it('shows how far trades and the ledger are read, what was new, and the next scheduled read', async () => {
    renderPortfolio();

    const line = await region();
    expect(await line.findByText('Trades read up to 11 Oct 2026')).toBeInTheDocument();
    expect(line.getByText(/— 4 new/)).toBeInTheDocument();
    expect(line.getByText('Ledger read up to 11 Oct 2026')).toBeInTheDocument();
    expect(line.getByText(/— 7 new/)).toBeInTheDocument();
    expect(line.getByText(/Next scheduled read .* IST/)).toBeInTheDocument();
    expect(await syncButton()).toBeEnabled();
  });

  it('says so when nothing was read yet and nothing is planned', async () => {
    server.use(http.get(statusPath, () => HttpResponse.json(buildNeverReadStatus())));
    renderPortfolio();

    const line = await region();
    expect(await line.findByText('Trades not read yet')).toBeInTheDocument();
    expect(line.getByText('Ledger not read yet')).toBeInTheDocument();
    expect(line.getByText('No scheduled read is planned.')).toBeInTheDocument();
  });

  it('shows the progress of a running read and keeps both Sync buttons disabled', async () => {
    server.use(http.get(statusPath, () => HttpResponse.json(buildRunningStatus())));
    renderPortfolio();

    const line = await region();
    expect(await line.findByText('Reading trades…')).toBeInTheDocument();
    expect(line.getByText(/3 of 12 windows done/)).toBeInTheDocument();
    expect(line.getByRole('status')).toHaveTextContent('Reading trades…');
    expect(line.queryByRole('alert')).not.toBeInTheDocument();
    expect(line.getByRole('button', { name: 'Reading…' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Sync now' })).toBeDisabled();
  });

  it('names the failure of the last run in plain words and keeps the read-up-to date', async () => {
    server.use(
      http.get(statusPath, () =>
        HttpResponse.json(
          buildBrokerStatus({
            trades: buildBrokerKind({
              lastRun: buildBrokerRun({ status: 'failed', error: 'Dhan did not answer in time.' }),
            }),
          }),
        ),
      ),
    );
    renderPortfolio();

    const line = await region();
    expect(await line.findByText('Trades read failed')).toBeInTheDocument();
    expect(line.getByText(/Dhan did not answer in time\. Read up to 11 Oct 2026\./)).toBeInTheDocument();
    expect(line.getByRole('alert')).toHaveTextContent('Trades read failed');
  });

  it('says how many holdings were rebuilt and which need review', async () => {
    server.use(
      http.get(statusPath, () =>
        HttpResponse.json(
          buildBrokerStatus({
            holdingsRebuilt: 2,
            holdingsNeedingReview: 1,
            needsReviewSymbols: ['INFY'],
            trades: buildBrokerKind({
              lastRun: buildBrokerRun({
                counts: buildBrokerCounts({
                  rebuilt: 2,
                  needsReview: 1,
                  notRebuilt: [{ symbol: 'INFY', reason: 'needs_review' }],
                }),
              }),
            }),
          }),
        ),
      ),
    );
    renderPortfolio();

    const line = await region();
    expect(await line.findByText(/2 holdings rebuilt, 1 needs review \(INFY\)/)).toBeInTheDocument();
  });

  it('shows an error with Retry when the status cannot be loaded', async () => {
    let fail = true;
    server.use(
      http.get(statusPath, () =>
        fail ? new HttpResponse(null, { status: 500 }) : HttpResponse.json(buildBrokerStatus()),
      ),
    );
    renderPortfolio();

    const line = await region();
    expect(await line.findByText('The trades and ledger status could not be loaded.')).toBeInTheDocument();
    fail = false;
    await userEvent.click(line.getByRole('button', { name: 'Retry' }));
    expect(await line.findByText('Trades read up to 11 Oct 2026')).toBeInTheDocument();
  });

  it('blocks the read while the account is not connected', async () => {
    const expired = withConnection(buildAccountDetail(), buildConnection({ status: 'expired' }));
    server.use(http.get('/api/accounts', () => HttpResponse.json([summaryOf(expired)])));
    renderPortfolio();

    expect(await syncButton()).toBeDisabled();
  });
});

describe('Sync trades & ledger', () => {
  it('starts the read, shows it running and reloads the holdings when it has ended, then stops polling', async () => {
    const timers = captureIntervals(brokerHistoryPollMs);
    let phase: 'idle' | 'running' | 'done' = 'idle';
    let posts = 0;
    let holdingsLoads = 0;
    server.use(
      http.get(statusPath, () =>
        HttpResponse.json(
          phase === 'running'
            ? buildRunningStatus()
            : phase === 'done'
              ? buildBrokerStatus({
                  trades: buildBrokerKind({
                    lastRun: buildBrokerRun({ id: brokerRunIds.trades2, counts: buildBrokerCounts({ stored: 9 }) }),
                  }),
                })
              : buildBrokerStatus(),
        ),
      ),
      http.post(syncPath, async ({ request }) => {
        posts += 1;
        expect(await request.text()).toBe('');
        phase = 'running';
        return HttpResponse.json({ trades: buildRunningStatus().trades.lastRun, ledger: null });
      }),
      http.get('/api/holdings', ({ request }) => {
        holdingsLoads += 1;
        return HttpResponse.json(searchHoldings(sampleHoldings, new URL(request.url).searchParams));
      }),
    );
    renderPortfolio();
    await screen.findByRole('gridcell', { name: 'RELIANCE' });
    const line = await region();
    expect(timers.count(brokerHistoryPollMs)).toBe(0);

    await userEvent.click(await syncButton());

    expect(await line.findByText('Reading trades…')).toBeInTheDocument();
    expect(posts).toBe(1);
    await waitFor(() => {
      expect(timers.count(brokerHistoryPollMs)).toBeGreaterThan(0);
    });
    const before = holdingsLoads;
    phase = 'done';
    timers.fire(brokerHistoryPollMs);

    expect(await line.findByText(/— 9 new/)).toBeInTheDocument();
    expect(line.queryByText('Reading trades…')).not.toBeInTheDocument();
    await waitFor(() => {
      expect(holdingsLoads).toBeGreaterThan(before);
      expect(timers.count(brokerHistoryPollMs)).toBe(0);
    });
    expect(await syncButton()).toBeEnabled();
  });

  it('reloads the holdings when a run that was already running at page load ends, and only once', async () => {
    const timers = captureIntervals(brokerHistoryPollMs);
    let running = true;
    let holdingsLoads = 0;
    server.use(
      http.get(statusPath, () =>
        HttpResponse.json(
          running
            ? buildRunningStatus()
            : buildBrokerStatus({
                trades: buildBrokerKind({
                  lastRun: buildBrokerRun({ id: brokerRunIds.trades2, counts: buildBrokerCounts({ stored: 5 }) }),
                }),
              }),
        ),
      ),
      http.get('/api/holdings', ({ request }) => {
        holdingsLoads += 1;
        return HttpResponse.json(searchHoldings(sampleHoldings, new URL(request.url).searchParams));
      }),
    );
    renderPortfolio();
    await screen.findByRole('gridcell', { name: 'RELIANCE' });
    const line = await region();
    expect(await line.findByText('Reading trades…')).toBeInTheDocument();
    await waitFor(() => {
      expect(timers.count(brokerHistoryPollMs)).toBeGreaterThan(0);
    });
    const before = holdingsLoads;

    running = false;
    timers.fire(brokerHistoryPollMs);

    expect(await line.findByText(/— 5 new/)).toBeInTheDocument();
    await waitFor(() => {
      expect(holdingsLoads).toBe(before + 1);
    });
    // Nothing else asks again: the settled status that the reload itself reads is not a second end.
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(holdingsLoads).toBe(before + 1);
  });

  it('does not reload the holdings for a status that is idle when the page opens', async () => {
    let holdingsLoads = 0;
    server.use(
      http.get('/api/holdings', ({ request }) => {
        holdingsLoads += 1;
        return HttpResponse.json(searchHoldings(sampleHoldings, new URL(request.url).searchParams));
      }),
    );
    renderPortfolio();
    await screen.findByRole('gridcell', { name: 'RELIANCE' });
    const line = await region();
    expect(await line.findByText('Trades read up to 11 Oct 2026')).toBeInTheDocument();
    const loads = holdingsLoads;

    await new Promise((resolve) => setTimeout(resolve, 50));

    expect(holdingsLoads).toBe(loads);
  });

  it('tells to connect the account again when the Dhan sign-in has expired, with a link to its Connection tab', async () => {
    server.use(
      http.post(syncPath, () =>
        problemResponse({ title: 'The Dhan token has expired.', code: 'portfolio.token_expired' }, 409),
      ),
    );
    renderPortfolio();

    await userEvent.click(await syncButton());

    expect(await screen.findByText(/The Dhan sign-in of this account has expired\./)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open the Connection tab' })).toHaveAttribute(
      'href',
      `/accounts?account=${ids.main}&tab=connection`,
    );
  });

  it('says a sync is already running', async () => {
    server.use(
      http.post(syncPath, () =>
        problemResponse({ title: 'A sync of this account is running.', code: 'portfolio.sync_running' }, 409),
      ),
    );
    renderPortfolio();

    await userEvent.click(await syncButton());

    expect(
      await screen.findByText('A sync or import of this account is already running. Wait a moment for it to finish.'),
    ).toBeInTheDocument();
  });

  it('offers the way to the Roles tab when the Portfolio role is off', async () => {
    server.use(http.post(syncPath, () => problemResponse({ title: 'Role off.', code: 'portfolio.role_off' }, 409)));
    renderPortfolio();

    await userEvent.click(await syncButton());

    expect(await screen.findByText('Switch on the Portfolio role of this account first.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open the Roles tab' })).toBeInTheDocument();
  });
});
