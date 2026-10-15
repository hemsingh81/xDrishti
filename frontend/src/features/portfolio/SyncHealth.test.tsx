import { screen, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { buildBrokerKind, buildBrokerRun, buildBrokerStatus } from '@/test/brokerHistoryFixtures';
import { buildSyncStatus } from '@/test/fixtures';
import { pinClock } from '@/test/pinClock';
import { server } from '@/test/server';
import { renderPortfolio } from './testing';

// The retry times are counted from the clock ("retrying in 5 minutes"), and the fixtures are of Oct 2026: the clock is pinned.
pinClock('2026-10-15T06:30:00Z');

// What the sync lines say when a sync is being retried quietly, keeps failing, or has read earlier history (ADR 0027).
const statusPath = '/api/accounts/:id/sync/broker-history/status';
const holdingsPath = '/api/accounts/:id/holdings/sync-status';
const inMinutes = (minutes: number) => new Date(Date.now() + minutes * 60_000 - 15_000).toISOString();

const region = async () => within(await screen.findByRole('region', { name: 'Trades and ledger from Dhan' }));

describe('Sync lines: health', () => {
  it('says nothing about health while everything works', async () => {
    renderPortfolio();

    const line = await region();
    expect(await line.findByText('Trades read up to 11 Oct 2026')).toBeInTheDocument();
    expect(screen.queryByText(/retrying/)).not.toBeInTheDocument();
    expect(screen.queryByText(/failed for/)).not.toBeInTheDocument();
    expect(screen.queryByText(/History read back/)).not.toBeInTheDocument();
  });

  it('tells that the holdings sync is retrying by itself, with the minutes and the attempt', async () => {
    server.use(
      http.get(holdingsPath, () =>
        HttpResponse.json(
          buildSyncStatus({
            retryAt: inMinutes(5),
            attempts: 2,
            consecutiveFailedDays: 1,
            lastFailureReason: 'unavailable',
          }),
        ),
      ),
    );
    renderPortfolio();

    expect(await screen.findByText('Holdings sync: retrying in 5 minutes (attempt 2 of 3)')).toBeInTheDocument();
    expect(screen.queryByText(/failed for/)).not.toBeInTheDocument();
  });

  it('tells for how long the holdings sync has failed, and why in plain words, once no retry is pending', async () => {
    server.use(
      http.get(holdingsPath, () =>
        HttpResponse.json(buildSyncStatus({ consecutiveFailedDays: 3, lastFailureReason: 'rate_limited' })),
      ),
    );
    renderPortfolio();

    expect(
      await screen.findByText('Holdings sync: failed for 3 days: Dhan asked for fewer requests'),
    ).toBeInTheDocument();
  });

  it('shows the retry of the trades read and the long failure of the ledger read on their own lines', async () => {
    server.use(
      http.get(statusPath, () =>
        HttpResponse.json(
          buildBrokerStatus({
            trades: buildBrokerKind({ retryAt: inMinutes(1), attempts: 1, lastFailureReason: 'interrupted' }),
            ledger: buildBrokerKind({
              consecutiveFailedDays: 2,
              lastFailureReason: 'token_refused',
              lastRun: buildBrokerRun({ kind: 'ledger', status: 'failed', error: 'Dhan refused the access token.' }),
            }),
          }),
        ),
      ),
    );
    renderPortfolio();

    const line = await region();
    expect(await line.findByText(/retrying in 1 minute \(attempt 1 of 3\)/)).toHaveTextContent(/^Trades read up to/);
    expect(line.getByText(/failed for 2 days: Dhan refused the login/)).toHaveTextContent(/^Ledger read failed/);
  });

  it('says how far back the nightly deepening has read the history', async () => {
    server.use(http.get(statusPath, () => HttpResponse.json(buildBrokerStatus({ deepenedTo: '2023-03-03' }))));
    renderPortfolio();

    expect(await (await region()).findByText('History read back to 3 Mar 2023')).toBeInTheDocument();
  });

  it('says that no earlier history was found, with or without a date it reached', async () => {
    server.use(
      http.get(statusPath, () =>
        HttpResponse.json(buildBrokerStatus({ deepenedTo: '2024-01-01', deepenExhausted: true })),
      ),
    );
    const first = renderPortfolio();
    expect(
      await (await region()).findByText('History read back to 1 Jan 2024 — no earlier history found'),
    ).toBeInTheDocument();
    first.unmount();

    server.use(http.get(statusPath, () => HttpResponse.json(buildBrokerStatus({ deepenExhausted: true }))));
    renderPortfolio();
    expect(await (await region()).findByText('No earlier history found')).toBeInTheDocument();
  });
});
