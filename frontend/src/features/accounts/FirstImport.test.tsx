import { fireEvent, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildAccountDetail, buildConnection, buildFirstImportView, buildPortfolioSaved, ids } from '@/test/fixtures';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
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

function captureSaves(answer: () => ReturnType<typeof buildPortfolioSaved>) {
  const bodies: unknown[] = [];
  server.use(
    http.put('/api/accounts/:id/portfolio', async ({ request }) => {
      bodies.push(await request.json());
      return HttpResponse.json(answer());
    }),
  );
  return bodies;
}

const save = () => userEvent.click(screen.getByRole('button', { name: 'Save portfolio settings' }));

describe('Portfolio tab: first import date', () => {
  it('shows the date beside the bucket and the totals checkbox, with one helper line and no list', async () => {
    useAccount(connectedNoHoldings());
    renderPortfolioTab();

    expect(await screen.findByLabelText('First import date')).toHaveValue('2025-01-01');
    expect(screen.getByLabelText(/^Default bucket/)).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: 'Include in portfolio totals' })).toBeInTheDocument();
    expect(screen.getByText(/Holdings are imported when you save/)).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'First import' })).not.toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save portfolio settings' })).toBeEnabled();
  });

  it('says the import waits for the first sync when the account is not connected', async () => {
    useAccount({ ...connectedNoHoldings(), connection: buildConnection() });
    renderPortfolioTab();

    expect(await screen.findByLabelText('First import date')).toBeInTheDocument();
    expect(screen.getByText(/import on the first sync after connecting/)).toBeInTheDocument();
  });

  it('shows the stored date read-only once the account has holdings', async () => {
    renderPortfolioTab();

    expect(await screen.findByText(/Holdings refresh automatically/)).toBeInTheDocument();
    expect(screen.queryByLabelText('First import date')).not.toBeInTheDocument();
    expect(screen.getByText('2025-01-01')).toBeInTheDocument();
  });

  it('sends only the date (no rows) and reports how many holdings were imported', async () => {
    useAccount(connectedNoHoldings());
    const bodies = captureSaves(() => buildPortfolioSaved(buildAccountDetail(), buildFirstImportView({ imported: 3 })));
    renderPortfolioTab();

    fireEvent.change(await screen.findByLabelText('First import date'), { target: { value: '2024-06-03' } });
    await save();

    expect(await screen.findByText('Imported 3 holdings.')).toBeInTheDocument();
    expect(bodies).toEqual([
      {
        defaultBucket: '',
        includeInTotals: true,
        refreshMode: 'off',
        refreshDailyTimeIst: '16:00',
        firstImportDate: '2024-06-03',
      },
    ]);
  });

  it('warns when the settings were saved but the import failed', async () => {
    useAccount(connectedNoHoldings());
    captureSaves(() =>
      buildPortfolioSaved(
        buildAccountDetail(),
        buildFirstImportView({
          status: 'failed',
          imported: 0,
          message: 'Settings saved, but a sync is running. Save again or press Sync now.',
        }),
      ),
    );
    renderPortfolioTab();

    await screen.findByLabelText('First import date');
    await save();

    expect(
      await screen.findByText('Settings saved, but a sync is running. Save again or press Sync now.'),
    ).toBeInTheDocument();
    expect(screen.queryByText(/^Imported/)).not.toBeInTheDocument();
  });

  it('refuses a first import date in the future before asking the server', async () => {
    useAccount(connectedNoHoldings());
    const bodies = captureSaves(() => buildPortfolioSaved(buildAccountDetail()));
    renderPortfolioTab();

    fireEvent.change(await screen.findByLabelText('First import date'), { target: { value: '2999-01-01' } });
    await save();

    expect(await screen.findByText('The date cannot be in the future.')).toBeInTheDocument();
    await waitFor(() => {
      expect(bodies).toHaveLength(0);
    });
  });
});
