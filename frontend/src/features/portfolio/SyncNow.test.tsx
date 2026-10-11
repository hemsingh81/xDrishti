import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildSyncRun, buildSyncStatus, ids, searchHoldings, sampleHoldings } from '@/test/fixtures';
import { problemResponse, server } from '@/test/server';
import { renderPortfolio } from './testing';

const syncPath = '/api/accounts/:id/holdings/sync';

async function pressSyncNow() {
  const button = await screen.findByRole('button', { name: 'Sync now' });
  await vi.waitFor(() => {
    expect(button).toBeEnabled();
  });
  await userEvent.click(button);
}

describe('Sync now', () => {
  it('syncs without a confirmation, tells how it went and reloads the figures and the status', async () => {
    const bodies: string[] = [];
    let holdingsLoads = 0;
    let statusLoads = 0;
    server.use(
      http.post(syncPath, async ({ request }) => {
        bodies.push(await request.text());
        return HttpResponse.json(buildSyncRun({ newCount: 2, increasedCount: 1, closedCount: 0 }));
      }),
      http.get('/api/holdings', ({ request }) => {
        holdingsLoads += 1;
        return HttpResponse.json(searchHoldings(sampleHoldings, new URL(request.url).searchParams));
      }),
      http.get('/api/accounts/:id/holdings/sync-status', () => {
        statusLoads += 1;
        return HttpResponse.json(buildSyncStatus());
      }),
    );
    renderPortfolio();
    await screen.findByRole('gridcell', { name: 'RELIANCE' });
    const holdingsBefore = holdingsLoads;
    const statusBefore = statusLoads;

    await pressSyncNow();

    expect(await screen.findByText('Synced: 2 new, 1 increased.')).toBeInTheDocument();
    expect(bodies).toEqual(['']);
    await vi.waitFor(() => {
      expect(holdingsLoads).toBeGreaterThan(holdingsBefore);
      expect(statusLoads).toBeGreaterThan(statusBefore);
    });
  });

  it('shows progress while the sync runs', async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.post(syncPath, async () => {
        await gate;
        return HttpResponse.json(buildSyncRun());
      }),
    );
    renderPortfolio();

    await pressSyncNow();

    const busy = await screen.findByRole('button', { name: 'Syncing…' });
    expect(busy).toBeDisabled();
    release();
    expect(await screen.findByRole('button', { name: 'Sync now' })).toBeInTheDocument();
  });

  it('reports a sync that ended as failed', async () => {
    server.use(
      http.post(syncPath, () =>
        HttpResponse.json(buildSyncRun({ status: 'failed', error: 'Dhan did not answer.', newCount: 0 })),
      ),
    );
    renderPortfolio();

    await pressSyncNow();

    expect(await screen.findByText('Sync failed: Dhan did not answer..')).toBeInTheDocument();
  });

  it('says a sync is already running', async () => {
    server.use(
      http.post(syncPath, () =>
        problemResponse(
          { title: 'A holdings sync or import of this account is already running.', code: 'portfolio.sync_running' },
          409,
        ),
      ),
    );
    renderPortfolio();

    await pressSyncNow();

    expect(
      await screen.findByText('A sync or import of this account is already running. Wait a moment for it to finish.'),
    ).toBeInTheDocument();
  });

  it('tells to connect the account first, with a link to its Connection tab', async () => {
    server.use(
      http.post(syncPath, () =>
        problemResponse({ title: 'The account is not connected to Dhan.', code: 'portfolio.not_connected' }, 409),
      ),
    );
    renderPortfolio();

    await pressSyncNow();

    expect(await screen.findByText(/Connect the account first\. Dhan is not signed in/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open the Connection tab' })).toHaveAttribute(
      'href',
      `/accounts?account=${ids.main}&tab=connection`,
    );
  });

  it('shows the API message for any other refusal', async () => {
    server.use(
      http.post(syncPath, () =>
        problemResponse({ title: 'The sync failed unexpectedly.', code: 'portfolio.sync_failed' }, 500),
      ),
    );
    renderPortfolio();

    await pressSyncNow();

    expect(await screen.findByText('The sync failed unexpectedly.')).toBeInTheDocument();
  });

  it('says so when Dhan cannot be reached', async () => {
    server.use(http.post(syncPath, () => problemResponse({ title: 'Dhan error', code: 'accounts.broker_error' }, 502)));
    renderPortfolio();

    await pressSyncNow();

    expect(await screen.findByText('Dhan could not be reached right now. Try again in a moment.')).toBeInTheDocument();
  });
});

describe('Sync now: the safeguard', () => {
  const refusal = (
    listedHoldings: number,
    openHoldings: number,
    wouldClose: number,
    code = 'portfolio.confirmation_required',
  ) =>
    problemResponse(
      { title: 'Confirm to continue.', code, status: 409, listedHoldings, openHoldings, wouldClose },
      409,
    );
  const parse = (text: string) => (text ? (JSON.parse(text) as unknown) : null);

  it('asks before closing everything and sends the numbers it showed along with the confirmation', async () => {
    const bodies: string[] = [];
    server.use(
      http.post(syncPath, async ({ request }) => {
        const text = await request.text();
        bodies.push(text);
        return text ? HttpResponse.json(buildSyncRun({ closedCount: 5, newCount: 0 })) : refusal(0, 5, 5);
      }),
    );
    renderPortfolio();

    await pressSyncNow();

    const dialog = await screen.findByRole('dialog', { name: 'Close these holdings?' });
    expect(
      within(dialog).getByText(
        /Dhan listed 0 holdings while 5 are open — all of them would be closed\. Confirm only if that is right\./,
      ),
    ).toBeInTheDocument();
    expect(within(dialog).queryByText(/changed since you looked/)).not.toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirm and sync' }));

    expect(await screen.findByText('Synced: 5 closed.')).toBeInTheDocument();
    expect(bodies.map(parse)).toEqual([null, { confirm: true, expectedOpenHoldings: 5, expectedWouldClose: 5 }]);
    await vi.waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
  });

  it('words a partial answer by what Dhan listed', async () => {
    server.use(http.post(syncPath, () => refusal(2, 6, 4)));
    renderPortfolio();

    await pressSyncNow();

    expect(
      await screen.findByText(
        /Dhan listed 2 holdings but 4 of your 6 open holdings are missing — they would be closed\./,
      ),
    ).toBeInTheDocument();
  });

  it('reopens the confirmation with the fresh numbers when the answer changed, and does not retry by itself', async () => {
    const bodies: string[] = [];
    server.use(
      http.post(syncPath, async ({ request }) => {
        const text = await request.text();
        bodies.push(text);
        if (!text) return refusal(0, 5, 5);
        const sent = JSON.parse(text) as { expectedWouldClose: number };
        return sent.expectedWouldClose === 5
          ? refusal(1, 5, 4, 'portfolio.confirmation_stale')
          : HttpResponse.json(buildSyncRun({ closedCount: 4, newCount: 0 }));
      }),
    );
    renderPortfolio();
    await pressSyncNow();
    await userEvent.click(await screen.findByRole('button', { name: 'Confirm and sync' }));

    const dialog = await screen.findByRole('dialog', { name: 'Close these holdings?' });
    expect(
      await within(dialog).findByText('Dhan’s answer changed since you looked — review again.'),
    ).toBeInTheDocument();
    expect(
      within(dialog).getByText(/Dhan listed 1 holding but 4 of your 5 open holdings are missing/),
    ).toBeInTheDocument();
    expect(bodies).toHaveLength(2); // no automatic third request

    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirm and sync' }));

    expect(await screen.findByText('Synced: 4 closed.')).toBeInTheDocument();
    expect(bodies.map(parse).at(-1)).toEqual({ confirm: true, expectedOpenHoldings: 5, expectedWouldClose: 4 });
  });

  it('changes nothing when the owner cancels', async () => {
    let posts = 0;
    server.use(
      http.post(syncPath, () => {
        posts += 1;
        return refusal(0, 5, 5);
      }),
    );
    renderPortfolio();
    await pressSyncNow();
    const dialog = await screen.findByRole('dialog', { name: 'Close these holdings?' });

    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));

    await vi.waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(posts).toBe(1);
    expect(screen.getByRole('button', { name: 'Sync now' })).toBeEnabled();
  });

  it('shows what went wrong when the confirmed sync is refused for another reason', async () => {
    server.use(
      http.post(syncPath, async ({ request }) =>
        (await request.text())
          ? problemResponse({ title: 'x', code: 'portfolio.not_connected' }, 409)
          : refusal(0, 5, 5),
      ),
    );
    renderPortfolio();
    await pressSyncNow();
    await userEvent.click(await screen.findByRole('button', { name: 'Confirm and sync' }));

    expect(await screen.findByText(/Connect the account first\. Dhan is not signed in/)).toBeInTheDocument();
    await vi.waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
  });
});
