import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import {
  buildAccountDetail,
  buildConnected,
  buildPortfolioSaved,
  buildSwingAccount,
  ids,
  summaryOf,
} from '@/test/fixtures';
import { renderWithProviders } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { ToastProvider } from '@/shared/ui/ToastProvider';
import { PortfolioRefreshDialog } from './components/PortfolioRefreshDialog';

const daily = (time: string) => ({ refreshMode: 'daily', refreshDailyTimeIst: time });

function renderDialog(onSaved: () => void = () => undefined) {
  return renderWithProviders(
    <ToastProvider>
      <PortfolioRefreshDialog onClose={() => undefined} onSaved={onSaved} subtitle="Next sync: Tue 13 Oct 16:00 IST" />
    </ToastProvider>,
  );
}

/** The accounts with Portfolio on: the main one (has holdings), and the swing one with Portfolio on and no holdings. */
function withSwingPortfolio(swingOverrides: Parameters<typeof buildSwingAccount>[0] = {}) {
  const swing = buildSwingAccount({
    roles: { ...buildSwingAccount().roles, portfolio: { enabled: true } },
    ...swingOverrides,
  });
  server.use(
    http.get('/api/accounts', () => HttpResponse.json([summaryOf(buildAccountDetail()), summaryOf(swing)])),
    http.get('/api/accounts/:id', ({ params }) =>
      HttpResponse.json(params.id === ids.swing ? swing : buildAccountDetail()),
    ),
  );
  return swing;
}

const form = (name: string) => screen.findByRole('form', { name: `Automatic refresh: ${name}` });

describe('Account sync settings', () => {
  it('lists the accounts that have the Portfolio role with their current refresh, and the next sync', async () => {
    renderDialog();

    const dialog = await screen.findByRole('dialog', { name: 'Account sync settings' });
    expect(within(dialog).getByText('Next sync: Tue 13 Oct 16:00 IST')).toBeInTheDocument();
    // Test Dhan Swing has no Portfolio role.
    const main = await form('Test Dhan Main');
    expect(within(dialog).queryByRole('form', { name: /Test Dhan Swing/ })).not.toBeInTheDocument();
    expect(within(main).getByRole('radio', { name: 'Off' })).toBeChecked();
    expect(within(main).getByLabelText('Time of day (IST)')).toHaveValue('16:00');
    expect(within(main).getByLabelText('Time of day (IST)')).toBeDisabled();
    expect(within(main).getByRole('button', { name: 'Save: Test Dhan Main' })).toBeDisabled();
  });

  it('saves Daily at a time and sends every other setting back as stored, without a bucket or a first import', async () => {
    const bodies: unknown[] = [];
    let saved = 0;
    const stored = buildAccountDetail({
      portfolio: { ...buildAccountDetail().portfolio, includeInTotals: false, firstImportDate: '2024-04-01' },
    });
    // What the API answers after the save: the account with its new refresh settings and a new `updatedAt`.
    const after = buildAccountDetail({
      updatedAt: '2026-10-13T11:00:00Z',
      portfolio: { ...stored.portfolio, ...daily('09:45') },
    });
    let current = stored;
    server.use(
      http.get('/api/accounts/:id', () => HttpResponse.json(current)),
      http.put('/api/accounts/:id/portfolio', async ({ request }) => {
        bodies.push(await request.json());
        current = after;
        return HttpResponse.json(buildPortfolioSaved(after));
      }),
    );
    renderDialog(() => {
      saved += 1;
    });

    const main = await form('Test Dhan Main');
    await userEvent.click(within(main).getByRole('radio', { name: 'Daily at' }));
    const time = within(main).getByLabelText('Time of day (IST)');
    expect(time).toBeEnabled();
    await userEvent.clear(time);
    await userEvent.type(time, '09:45');
    await userEvent.click(within(main).getByRole('button', { name: 'Save: Test Dhan Main' }));

    expect(await screen.findByText('Portfolio settings saved.')).toBeInTheDocument();
    expect(bodies).toEqual([{ includeInTotals: false, ...daily('09:45'), firstImportDate: '2024-04-01' }]);
    expect(bodies[0]).not.toHaveProperty('defaultBucket');
    expect(bodies[0]).not.toHaveProperty('firstImport');
    // The account has a new `updatedAt`: the opener is still told, and the form shows what was saved, as clean.
    await waitFor(() => {
      expect(saved).toBe(1);
    });
    const again = await form('Test Dhan Main');
    expect(within(again).getByRole('radio', { name: 'Daily at' })).toBeChecked();
    expect(within(again).getByLabelText('Time of day (IST)')).toHaveValue('09:45');
    expect(within(again).getByRole('button', { name: 'Save: Test Dhan Main' })).toBeDisabled();
    // The dialog stays open to set the next account.
    expect(screen.getByRole('dialog', { name: 'Account sync settings' })).toBeInTheDocument();
  });

  it('keeps what was typed when the account is read again in the background', async () => {
    let updatedAt = '2026-10-09T09:00:00Z';
    server.use(http.get('/api/accounts/:id', () => HttpResponse.json(buildAccountDetail({ updatedAt }))));
    const { client } = renderDialog();

    const main = await form('Test Dhan Main');
    await userEvent.click(within(main).getByRole('radio', { name: 'Daily at' }));
    updatedAt = '2026-10-13T11:30:00Z';
    await client.invalidateQueries({ queryKey: ['accounts', 'detail'] });

    const again = await form('Test Dhan Main');
    expect(within(again).getByRole('radio', { name: 'Daily at' })).toBeChecked();
    expect(within(again).getByRole('button', { name: 'Save: Test Dhan Main' })).toBeEnabled();
  });

  it('follows the stored setting when nothing was typed and it changes elsewhere', async () => {
    let stored = buildAccountDetail();
    server.use(http.get('/api/accounts/:id', () => HttpResponse.json(stored)));
    const { client } = renderDialog();
    expect(within(await form('Test Dhan Main')).getByRole('radio', { name: 'Off' })).toBeChecked();

    stored = buildAccountDetail({
      updatedAt: '2026-10-13T11:30:00Z',
      portfolio: { ...stored.portfolio, ...daily('17:15') },
    });
    await client.invalidateQueries({ queryKey: ['accounts', 'detail'] });

    await waitFor(() => {
      expect(screen.getByRole('radio', { name: 'Daily at' })).toBeChecked();
    });
    expect(screen.getByLabelText('Time of day (IST)')).toHaveValue('17:15');
  });

  it('does not save while the stored settings are being read again', async () => {
    let slow = false;
    server.use(
      http.get('/api/accounts/:id', async () => {
        if (slow) await new Promise<never>(() => undefined);
        return HttpResponse.json(buildAccountDetail());
      }),
    );
    const { client } = renderDialog();
    const main = await form('Test Dhan Main');
    await userEvent.click(within(main).getByRole('radio', { name: 'Daily at' }));
    expect(within(main).getByRole('button', { name: 'Save: Test Dhan Main' })).toBeEnabled();

    slow = true;
    void client.invalidateQueries({ queryKey: ['accounts', 'detail'] });

    await waitFor(() => {
      expect(within(main).getByRole('button', { name: 'Save: Test Dhan Main' })).toBeDisabled();
    });
  });

  it('shows a daily setting and saves switching it off', async () => {
    const bodies: unknown[] = [];
    server.use(
      http.get('/api/accounts/:id', () =>
        HttpResponse.json(buildAccountDetail({ portfolio: { ...buildAccountDetail().portfolio, ...daily('18:15') } })),
      ),
      http.put('/api/accounts/:id/portfolio', async ({ request }) => {
        bodies.push(await request.json());
        return HttpResponse.json(buildPortfolioSaved(buildAccountDetail()));
      }),
    );
    renderDialog();

    const main = await form('Test Dhan Main');
    expect(within(main).getByRole('radio', { name: 'Daily at' })).toBeChecked();
    expect(within(main).getByLabelText('Time of day (IST)')).toHaveValue('18:15');
    await userEvent.click(within(main).getByRole('radio', { name: 'Off' }));
    await userEvent.click(within(main).getByRole('button', { name: 'Save: Test Dhan Main' }));

    await screen.findByText('Portfolio settings saved.');
    // The time is kept while the refresh is off.
    expect(bodies).toEqual([
      { includeInTotals: true, refreshMode: 'off', refreshDailyTimeIst: '18:15', firstImportDate: '2025-01-01' },
    ]);
  });

  it('refuses a daily refresh without a time before asking the server', async () => {
    let puts = 0;
    server.use(
      http.put('/api/accounts/:id/portfolio', () => {
        puts += 1;
        return HttpResponse.json(buildPortfolioSaved(buildAccountDetail()));
      }),
    );
    renderDialog();

    const main = await form('Test Dhan Main');
    await userEvent.click(within(main).getByRole('radio', { name: 'Daily at' }));
    await userEvent.clear(within(main).getByLabelText('Time of day (IST)'));
    await userEvent.click(within(main).getByRole('button', { name: 'Save: Test Dhan Main' }));

    expect(await within(main).findByText('Enter a time as HH:MM, for example 16:00.')).toBeInTheDocument();
    expect(puts).toBe(0);
  });

  it('says what the server refused: a field message, and an outage', async () => {
    let call = 0;
    server.use(
      http.put('/api/accounts/:id/portfolio', () => {
        call += 1;
        return call === 1
          ? problemResponse(
              {
                title: 'Some values are not valid.',
                code: 'accounts.invalid',
                errors: { refreshDailyTimeIst: ['Use HH:mm, for example 16:00.'] },
              },
              400,
            )
          : problemResponse({ title: 'The server is busy.', code: 'x' }, 503);
      }),
    );
    renderDialog();

    const main = await form('Test Dhan Main');
    await userEvent.click(within(main).getByRole('radio', { name: 'Daily at' }));
    await userEvent.click(within(main).getByRole('button', { name: 'Save: Test Dhan Main' }));
    expect(await within(main).findByText('Use HH:mm, for example 16:00.')).toBeInTheDocument();

    await userEvent.click(within(main).getByRole('button', { name: 'Save: Test Dhan Main' }));
    expect(await within(main).findByText('The server is busy.')).toBeInTheDocument();
    expect(screen.queryByText('Portfolio settings saved.')).not.toBeInTheDocument();
  });

  it('shows an account that would import its holdings on saving read-only, with a link to its Portfolio tab', async () => {
    let puts = 0;
    withSwingPortfolio({
      connection: buildConnected(),
      tokenStatus: 'connected',
      portfolio: { ...buildSwingAccount().portfolio, enabled: true, hasHoldings: false },
    });
    server.use(
      http.put('/api/accounts/:id/portfolio', () => {
        puts += 1;
        return HttpResponse.json(buildPortfolioSaved(buildAccountDetail()));
      }),
    );
    renderDialog();

    const swing = await form('Test Dhan Swing');
    expect(within(swing).getByRole('radio', { name: 'Off' })).toBeDisabled();
    expect(within(swing).queryByRole('button', { name: /^Save/ })).not.toBeInTheDocument();
    expect(within(swing).getByText(/saving here would also import them for the first time/)).toBeInTheDocument();
    expect(within(swing).getByRole('link', { name: 'Change in Accounts: Test Dhan Swing' })).toHaveAttribute(
      'href',
      `/accounts?account=${ids.swing}&tab=portfolio`,
    );
    // The account that already has holdings can still be changed.
    expect(within(await form('Test Dhan Main')).getByRole('radio', { name: 'Off' })).toBeEnabled();
    expect(puts).toBe(0);
  });

  it('lets an account that is not connected be set (nothing is imported until it connects)', async () => {
    withSwingPortfolio({ portfolio: { ...buildSwingAccount().portfolio, enabled: true, hasHoldings: false } });
    renderDialog();

    const swing = await form('Test Dhan Swing');
    expect(within(swing).getByRole('radio', { name: 'Off' })).toBeEnabled();
    expect(within(swing).getByRole('button', { name: 'Save: Test Dhan Swing' })).toBeDisabled();
  });

  it('shows a loading state, then an error with Retry when the accounts cannot be read', async () => {
    let fail = true;
    server.use(
      http.get('/api/accounts', () =>
        fail
          ? problemResponse({ title: 'Down.', code: 'x' }, 503)
          : HttpResponse.json([summaryOf(buildAccountDetail())]),
      ),
    );
    renderDialog();

    expect(await screen.findByRole('progressbar', { name: 'Loading accounts' })).toBeInTheDocument();
    expect(await screen.findByText('The accounts could not be loaded.')).toBeInTheDocument();
    fail = false;
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));

    expect(await form('Test Dhan Main')).toBeInTheDocument();
  });

  it('says so when one account’s settings cannot be read, and reads them again on Retry', async () => {
    let fail = true;
    server.use(
      http.get('/api/accounts/:id', () =>
        fail ? problemResponse({ title: 'Down.', code: 'x' }, 503) : HttpResponse.json(buildAccountDetail()),
      ),
    );
    renderDialog();

    expect(await screen.findByText('The settings of Test Dhan Main could not be loaded.')).toBeInTheDocument();
    fail = false;
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));

    expect(await form('Test Dhan Main')).toBeInTheDocument();
  });

  it('says so when no account has the Portfolio role', async () => {
    server.use(http.get('/api/accounts', () => HttpResponse.json([summaryOf(buildSwingAccount())])));
    renderDialog();

    expect(await screen.findByText('No account has the Portfolio role')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Accounts' })).toHaveAttribute('href', '/accounts');
  });
});
