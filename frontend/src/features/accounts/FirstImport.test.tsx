import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import {
  buildAccountDetail,
  buildConnection,
  buildFirstImportView,
  buildImportPreview,
  buildPortfolioSaved,
  ids,
} from '@/test/fixtures';
import { renderRoutes } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { AccountsPage } from './components/AccountsPage';

const connectedNoHoldings = () => {
  const base = buildAccountDetail();
  return buildAccountDetail({ portfolio: { ...base.portfolio, hasHoldings: false } });
};

function renderPortfolioTab() {
  return renderRoutes([{ path: '/accounts', element: <AccountsPage /> }], {
    initialPath: `/accounts?account=${ids.main}&tab=portfolio`,
  });
}

function useAccount(account: ReturnType<typeof buildAccountDetail>) {
  server.use(http.get('/api/accounts/:id', () => HttpResponse.json(account)));
}

function usePreview() {
  let calls = 0;
  server.use(
    http.post('/api/accounts/:id/holdings/preview', () => {
      calls += 1;
      return HttpResponse.json(buildImportPreview());
    }),
  );
  return () => calls;
}

const save = () => userEvent.click(screen.getByRole('button', { name: 'Save portfolio settings' }));

describe('Portfolio tab: first import', () => {
  it('lists the holdings with a date each when connected and without holdings', async () => {
    useAccount(connectedNoHoldings());
    usePreview();
    renderPortfolioTab();

    const region = await screen.findByRole('region', { name: 'First import' });
    expect(within(region).getByLabelText('First import date')).toHaveValue('2025-01-01');
    const table = await within(region).findByRole('table', { name: 'Holdings at Dhan' });
    expect(within(table).getByLabelText('Acquired on, RELIANCE')).toHaveValue('2025-01-01');
    expect(within(table).getByLabelText('Acquired on, TCS')).toHaveValue('2025-01-01');
    expect(within(region).getByRole('button', { name: 'Apply date to all' })).toBeInTheDocument();
  });

  it('shows only the date and a note when the account is not connected, and asks Dhan for nothing', async () => {
    const account = connectedNoHoldings();
    useAccount({ ...account, connection: buildConnection() });
    const calls = usePreview();
    renderPortfolioTab();

    const region = await screen.findByRole('region', { name: 'First import' });
    expect(within(region).getByLabelText('First import date')).toBeInTheDocument();
    expect(within(region).getByText(/not connected to Dhan/)).toBeInTheDocument();
    expect(within(region).queryByRole('table')).not.toBeInTheDocument();
    expect(calls()).toBe(0);
  });

  it('shows a read-only note instead of the list once the account has holdings', async () => {
    const calls = usePreview();
    renderPortfolioTab();

    const region = await screen.findByRole('region', { name: 'First import' });
    expect(within(region).getByText(/Holdings are refreshed automatically/)).toBeInTheDocument();
    expect(within(region).queryByLabelText('First import date')).not.toBeInTheDocument();
    expect(within(region).queryByRole('table')).not.toBeInTheDocument();
    expect(calls()).toBe(0);
  });

  it('shows an error with Retry when Dhan’s holdings cannot be fetched', async () => {
    useAccount(connectedNoHoldings());
    server.use(http.post('/api/accounts/:id/holdings/preview', () => new HttpResponse(null, { status: 500 })));
    renderPortfolioTab();

    expect(await screen.findByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });

  it('keeps Save disabled while Dhan’s holdings are loading', async () => {
    useAccount(connectedNoHoldings());
    server.use(http.post('/api/accounts/:id/holdings/preview', () => new Promise<never>(() => undefined)));
    renderPortfolioTab();

    await screen.findByLabelText('Fetching holdings from Dhan');
    expect(screen.getByRole('button', { name: 'Save portfolio settings' })).toBeDisabled();
  });

  it('warns that the first sync imports everything when the holdings failed to load, and still allows Save', async () => {
    useAccount(connectedNoHoldings());
    server.use(http.post('/api/accounts/:id/holdings/preview', () => new HttpResponse(null, { status: 500 })));
    const bodies: { firstImport?: unknown }[] = [];
    server.use(
      http.put('/api/accounts/:id/portfolio', async ({ request }) => {
        bodies.push((await request.json()) as { firstImport?: unknown });
        return HttpResponse.json(buildPortfolioSaved(buildAccountDetail()));
      }),
    );
    renderPortfolioTab();

    const region = await screen.findByRole('region', { name: 'First import' });
    expect(await within(region).findByText(/first sync imports all holdings with the first import date/)).toBeVisible();
    expect(screen.getByRole('button', { name: 'Save portfolio settings' })).toBeEnabled();
    await save();
    await waitFor(() => {
      expect(bodies).toHaveLength(1);
    });
    expect(bodies[0]?.firstImport).toBeUndefined();
  });

  it('says all listed holdings are imported and what the first daily refresh does', async () => {
    useAccount(connectedNoHoldings());
    usePreview();
    renderPortfolioTab();

    const region = await screen.findByRole('region', { name: 'First import' });
    await within(region).findByRole('table', { name: 'Holdings at Dhan' });
    expect(within(region).getByText(/All listed holdings are imported/)).toBeInTheDocument();
    expect(
      within(region).getByText(/Daily refresh is turned on and no holdings are imported yet, the first refresh/),
    ).toBeInTheDocument();
    expect(within(region).getByRole('button', { name: 'Apply date to selected' })).toBeDisabled();
  });

  it('sends the first import date and the chosen dates, then reports how many were imported', async () => {
    useAccount(connectedNoHoldings());
    usePreview();
    const bodies: unknown[] = [];
    server.use(
      http.put('/api/accounts/:id/portfolio', async ({ request }) => {
        bodies.push(await request.json());
        return HttpResponse.json(buildPortfolioSaved(buildAccountDetail(), buildFirstImportView({ imported: 3 })));
      }),
    );
    renderPortfolioTab();

    const table = await screen.findByRole('table', { name: 'Holdings at Dhan' });
    fireEvent.change(screen.getByLabelText('First import date'), { target: { value: '2024-06-03' } });
    expect(within(table).getByLabelText('Acquired on, TCS')).toHaveValue('2024-06-03');
    fireEvent.change(within(table).getByLabelText('Acquired on, RELIANCE'), { target: { value: '2023-02-01' } });
    await save();

    expect(await screen.findByText('Imported 3 holdings.')).toBeInTheDocument();
    expect(bodies).toEqual([
      {
        defaultBucket: '',
        includeInTotals: true,
        refreshMode: 'off',
        refreshDailyTimeIst: '16:00',
        firstImportDate: '2024-06-03',
        firstImport: {
          rows: [
            { key: 'INE002A01018', acquiredOn: '2023-02-01' },
            { key: 'INE467B01029', acquiredOn: null },
            { key: '500999:BSE', acquiredOn: null },
          ],
        },
      },
    ]);
  });

  it('gives every holding the bulk date', async () => {
    useAccount(connectedNoHoldings());
    usePreview();
    const bodies: { firstImport?: { rows: unknown[] } }[] = [];
    server.use(
      http.put('/api/accounts/:id/portfolio', async ({ request }) => {
        bodies.push((await request.json()) as { firstImport?: { rows: unknown[] } });
        return HttpResponse.json(buildPortfolioSaved(buildAccountDetail(), buildFirstImportView()));
      }),
    );
    renderPortfolioTab();

    await screen.findByRole('table', { name: 'Holdings at Dhan' });
    fireEvent.change(screen.getByLabelText('Date to apply'), { target: { value: '2022-05-04' } });
    await userEvent.click(screen.getByRole('button', { name: 'Apply date to all' }));
    await save();

    await screen.findByText('Imported 3 holdings.');
    expect(bodies[0]?.firstImport?.rows).toEqual([
      { key: 'INE002A01018', acquiredOn: '2022-05-04' },
      { key: 'INE467B01029', acquiredOn: '2022-05-04' },
      { key: '500999:BSE', acquiredOn: '2022-05-04' },
    ]);
  });

  it('warns when the settings were saved but the import failed', async () => {
    useAccount(connectedNoHoldings());
    usePreview();
    server.use(
      http.put('/api/accounts/:id/portfolio', () =>
        HttpResponse.json(
          buildPortfolioSaved(
            buildAccountDetail(),
            buildFirstImportView({
              status: 'failed',
              imported: 0,
              errorCode: 'portfolio.import_failed',
              cause: 'portfolio.sync_running',
              message: 'Settings saved, but a sync is running. Save again or press Sync now.',
            }),
          ),
        ),
      ),
    );
    renderPortfolioTab();

    await screen.findByRole('table', { name: 'Holdings at Dhan' });
    await save();

    expect(
      await screen.findByText('Settings saved, but a sync is running. Save again or press Sync now.'),
    ).toBeInTheDocument();
    expect(screen.queryByText(/^Imported/)).not.toBeInTheDocument();
  });

  it('puts the server’s refusal of a date on its row', async () => {
    useAccount(connectedNoHoldings());
    usePreview();
    server.use(
      http.put('/api/accounts/:id/portfolio', () =>
        problemResponse(
          {
            title: 'Some values are not valid.',
            code: 'accounts.invalid',
            errors: { 'firstImport.rows[1].acquiredOn': ['The date cannot be in the future.'] },
          },
          400,
        ),
      ),
    );
    renderPortfolioTab();

    const table = await screen.findByRole('table', { name: 'Holdings at Dhan' });
    await save();

    const tcs = within(table).getByLabelText('Acquired on, TCS');
    expect(await within(table).findByText('The date cannot be in the future.')).toBeInTheDocument();
    expect(tcs).toBeInvalid();
  });

  it('refuses a first import date in the future before asking the server', async () => {
    useAccount(connectedNoHoldings());
    usePreview();
    let puts = 0;
    server.use(
      http.put('/api/accounts/:id/portfolio', () => {
        puts += 1;
        return HttpResponse.json(buildPortfolioSaved(buildAccountDetail()));
      }),
    );
    renderPortfolioTab();

    await screen.findByRole('table', { name: 'Holdings at Dhan' });
    fireEvent.change(screen.getByLabelText('First import date'), { target: { value: '2999-01-01' } });
    await save();

    expect(await screen.findByText('The date cannot be in the future.')).toBeInTheDocument();
    expect(puts).toBe(0);
  });
});
