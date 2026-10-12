import { fireEvent, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildAccountDetail, buildConnection, buildFirstImportView, buildPortfolioSaved, ids } from '@/test/fixtures';
import { renderRoutes } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { istDayIso } from '@/shared/lib/format';
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
  it('shows the date as a calendar beside the totals checkbox, with one helper line and no bucket', async () => {
    useAccount(connectedNoHoldings());
    renderPortfolioTab();

    const date = await screen.findByLabelText('First import date');
    expect(date).toHaveAttribute('type', 'date');
    expect(date).toHaveValue('2025-01-01');
    expect(date).toHaveAttribute('min', '1990-01-01');
    expect(date).toHaveAttribute('max', istDayIso());
    expect(screen.queryByLabelText(/bucket/i)).not.toBeInTheDocument();
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
    expect(screen.getByText(/first sync after connecting imports them with this date/)).toBeInTheDocument();
  });

  it('keeps the date editable once the account has holdings, and says what changing it does', async () => {
    renderPortfolioTab();

    const date = await screen.findByLabelText('First import date');
    expect(date).toBeEnabled();
    expect(date).toHaveValue('2025-01-01');
    expect(screen.getByText(/Changing it re-dates the holdings still on the default date\./)).toBeInTheDocument();
    expect(screen.queryByText(/Holdings are imported when you save/)).not.toBeInTheDocument();
    expect(screen.queryByText('First import date:')).not.toBeInTheDocument();

    fireEvent.change(date, { target: { value: '2024-03-04' } });
    expect(date).toHaveValue('2024-03-04');
  });

  it('sends the changed date for an account with holdings and says how many holdings were re-dated', async () => {
    const base = buildAccountDetail();
    const bodies = captureSaves(() =>
      buildPortfolioSaved(
        buildAccountDetail({ portfolio: { ...base.portfolio, firstImportDate: '2024-06-03' } }),
        buildFirstImportView({ status: 'skipped', imported: 0, defaultDated: 0, redated: 12 }),
      ),
    );
    renderPortfolioTab();

    fireEvent.change(await screen.findByLabelText('First import date'), { target: { value: '2024-06-03' } });
    await save();

    expect(await screen.findByText('Re-dated 12 holdings to 3 Jun 2024.')).toBeInTheDocument();
    expect(bodies).toEqual([
      { includeInTotals: true, refreshMode: 'off', refreshDailyTimeIst: '16:00', firstImportDate: '2024-06-03' },
    ]);
    expect(bodies[0]).not.toHaveProperty('defaultBucket');
    expect(await screen.findByLabelText('First import date')).toHaveValue('2024-06-03');
  });

  it('says holding in the singular for one re-dated holding', async () => {
    const base = buildAccountDetail();
    captureSaves(() =>
      buildPortfolioSaved(
        buildAccountDetail({ portfolio: { ...base.portfolio, firstImportDate: '2024-06-03' } }),
        buildFirstImportView({ status: 'skipped', imported: 0, defaultDated: 0, redated: 1 }),
      ),
    );
    renderPortfolioTab();

    fireEvent.change(await screen.findByLabelText('First import date'), { target: { value: '2024-06-03' } });
    await save();

    expect(await screen.findByText('Re-dated 1 holding to 3 Jun 2024.')).toBeInTheDocument();
  });

  it('adds a note when some lots could not be re-dated because they were closed before the new date', async () => {
    const base = buildAccountDetail();
    captureSaves(() =>
      buildPortfolioSaved(
        buildAccountDetail({ portfolio: { ...base.portfolio, firstImportDate: '2026-01-01' } }),
        buildFirstImportView({ status: 'skipped', imported: 0, defaultDated: 0, redated: 4, redateSkipped: 2 }),
      ),
    );
    renderPortfolioTab();

    fireEvent.change(await screen.findByLabelText('First import date'), { target: { value: '2026-01-01' } });
    await save();

    expect(
      await screen.findByText(
        'Re-dated 4 holdings to 1 Jan 2026. 2 lots could not be re-dated because they were closed before that date.',
      ),
    ).toBeInTheDocument();
  });

  it('still reports skipped lots when none could be re-dated', async () => {
    captureSaves(() =>
      buildPortfolioSaved(
        buildAccountDetail(),
        buildFirstImportView({ status: 'skipped', imported: 0, defaultDated: 0, redated: 0, redateSkipped: 1 }),
      ),
    );
    renderPortfolioTab();

    await screen.findByLabelText('First import date');
    await save();

    expect(
      await screen.findByText(
        'Portfolio settings saved. 1 lot could not be re-dated because it was closed before that date.',
      ),
    ).toBeInTheDocument();
    expect(screen.queryByText(/^Re-dated/)).not.toBeInTheDocument();
  });

  it('says only that the settings were saved when nothing was re-dated', async () => {
    captureSaves(() => buildPortfolioSaved(buildAccountDetail()));
    renderPortfolioTab();

    await screen.findByLabelText('First import date');
    await save();

    expect(await screen.findByText('Portfolio settings saved.')).toBeInTheDocument();
    expect(screen.queryByText(/Re-dated|could not be re-dated/)).not.toBeInTheDocument();
  });

  it('sends only the date (no rows) and reports how many holdings were imported', async () => {
    useAccount(connectedNoHoldings());
    const bodies = captureSaves(() => buildPortfolioSaved(buildAccountDetail(), buildFirstImportView({ imported: 3 })));
    renderPortfolioTab();

    fireEvent.change(await screen.findByLabelText('First import date'), { target: { value: '2024-06-03' } });
    await save();

    expect(await screen.findByText('Imported 3 holdings.')).toBeInTheDocument();
    expect(bodies[0]).not.toHaveProperty('defaultBucket');
    expect(bodies).toEqual([
      {
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

  it.each([
    ['in the future', '2999-01-01', 'The date cannot be in the future.'],
    ['before 1990', '1989-12-31', 'The date cannot be before 1 Jan 1990.'],
    ['empty', '', 'Enter a date.'],
  ])('refuses a first import date that is %s before asking the server', async (_name, value, message) => {
    const bodies = captureSaves(() => buildPortfolioSaved(buildAccountDetail()));
    renderPortfolioTab();

    fireEvent.change(await screen.findByLabelText('First import date'), { target: { value } });
    await save();

    expect(await screen.findByText(message)).toBeInTheDocument();
    expect(screen.getByLabelText('First import date')).toBeInvalid();
    await waitFor(() => {
      expect(bodies).toHaveLength(0);
    });
  });

  it('shows the server’s message on the date field', async () => {
    server.use(
      http.put('/api/accounts/:id/portfolio', () =>
        problemResponse(
          {
            title: 'Some values are not valid.',
            code: 'accounts.invalid',
            errors: { firstImportDate: ['The first import date cannot be in the future.'] },
          },
          400,
        ),
      ),
    );
    renderPortfolioTab();

    await screen.findByLabelText('First import date');
    await save();

    expect(await screen.findByText('The first import date cannot be in the future.')).toBeInTheDocument();
    expect(screen.getByLabelText('First import date')).toBeInvalid();
  });
});
