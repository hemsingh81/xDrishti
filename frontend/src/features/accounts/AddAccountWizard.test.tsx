import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { externalNavigation } from '@/shared/lib/externalNavigation';
import { buildAccountDetail, ids } from '@/test/fixtures';
import { renderRoutes } from '@/test/render';
import { createStepUpGate, dhanAuthorizeUrl, dhanRedirectUrl, problemResponse, server } from '@/test/server';
import { AccountsPage } from './components/AccountsPage';
import { istDayIso } from '@/shared/lib/format';

const newId = '00000000-0000-4000-8000-0000000000c3';
// Made-up values that look nothing like real keys; tests check they never show up on screen.
const fakeKey = 'fake-key-value-0001';
const fakeSecret = 'fake-secret-value-0002';

function renderPage() {
  return renderRoutes([{ path: '/accounts', element: <AccountsPage /> }], { initialPath: '/accounts' });
}

async function openWizard() {
  await userEvent.click(await screen.findByRole('button', { name: 'Add account' }));
  return screen.findByRole('dialog', { name: 'Add Dhan account' });
}

const next = (dialog: HTMLElement) => userEvent.click(within(dialog).getByRole('button', { name: 'Next' }));
const back = (dialog: HTMLElement) => userEvent.click(within(dialog).getByRole('button', { name: 'Back' }));

async function fillDhanApp(dialog: HTMLElement) {
  await userEvent.type(within(dialog).getByLabelText(/^Client id/), '1000000001');
  await userEvent.type(within(dialog).getByLabelText(/^API key \(app_id\)/), fakeKey);
  await userEvent.type(within(dialog).getByLabelText(/^API secret \(app_secret\)/), fakeSecret);
  await next(dialog);
}

/** Roles: Trading and Data on (Portfolio is on by default). */
async function chooseRoles(dialog: HTMLElement) {
  await userEvent.click(within(dialog).getByRole('checkbox', { name: 'Trading' }));
  await userEvent.click(within(dialog).getByRole('checkbox', { name: 'Data' }));
  await next(dialog);
}

async function fillSettings(dialog: HTMLElement) {
  await userEvent.type(await within(dialog).findByLabelText(/^Capital \(₹\)/), '150000');
  await userEvent.click(within(dialog).getByRole('checkbox', { name: 'EQ_CNC' }));
  await next(dialog);
}

async function reachReview(dialog: HTMLElement) {
  await fillDhanApp(dialog);
  await next(dialog); // the suggested name is fine
  await chooseRoles(dialog);
  await fillSettings(dialog);
  expect(await within(dialog).findByText('Check the details, then create the account.')).toBeInTheDocument();
}

const created = () =>
  buildAccountDetail({
    id: newId,
    name: 'Dhan · Main',
    roles: {
      data: { enabled: true, primary: false, standby: true },
      trading: { enabled: true, mode: 'paper', profile: 'Standard' },
      portfolio: { enabled: true },
    },
  });

describe('Add-account wizard', () => {
  it('walks through the five steps and creates the account with its secrets after the password dialog', async () => {
    const gate = createStepUpGate();
    const bodies: unknown[] = [];
    server.use(
      gate.handler,
      http.post('/api/accounts', async ({ request }) => {
        const denied = gate.require();
        if (denied) {
          return denied;
        }
        bodies.push(await request.json());
        return HttpResponse.json(created(), { status: 201 });
      }),
    );
    const { router } = renderPage();
    const dialog = await openWizard();

    // Dhan app: its setup steps, redirect URL and fields; there is no broker to choose.
    expect(within(dialog).getByText('Step 1 of 5: Dhan app')).toBeInTheDocument();
    expect(within(dialog).queryByRole('radio')).not.toBeInTheDocument();
    expect(within(dialog).getByText(/Access DhanHQ APIs/)).toBeInTheDocument();
    expect(await within(dialog).findByText(dhanRedirectUrl)).toBeInTheDocument();
    expect(within(dialog).getByLabelText(/^API key generated on/)).toHaveValue(istDayIso());
    await fillDhanApp(dialog);

    // Identity: the name is suggested.
    expect(within(dialog).getByText('Step 2 of 5: Identity')).toBeInTheDocument();
    expect(within(dialog).getByLabelText(/^Display name/)).toHaveValue('Dhan · Main');
    await next(dialog);

    // Roles: a primary exists already, so a new Data account starts as standby.
    expect(within(dialog).getByRole('checkbox', { name: 'Portfolio' })).toBeChecked();
    await chooseRoles(dialog);

    expect(await within(dialog).findByRole('region', { name: 'Trading settings' })).toBeInTheDocument();
    expect(within(dialog).getByLabelText(/^Risk profile/)).toHaveDisplayValue('Standard');
    expect(within(dialog).queryByLabelText(/bucket/i)).not.toBeInTheDocument();
    expect(within(dialog).queryByLabelText(/history/i)).not.toBeInTheDocument();
    await fillSettings(dialog);

    await userEvent.click(within(dialog).getByRole('button', { name: 'Create account' }));
    const passwordDialog = await screen.findByRole('dialog', { name: 'Confirm your password' });
    await userEvent.type(within(passwordDialog).getByLabelText(/Password/), 'correct-password');
    await userEvent.click(within(passwordDialog).getByRole('button', { name: 'Confirm' }));

    expect(await screen.findByText('Dhan · Main was added. Connect it from the Connection tab.')).toBeInTheDocument();
    expect(screen.queryByRole('dialog', { name: 'Add Dhan account' })).not.toBeInTheDocument();
    expect(router.state.location.search).toBe(`?account=${newId}`);
    expect(router.state.location.search).not.toContain(fakeKey);
    expect(bodies).toEqual([
      {
        name: 'Dhan · Main',
        clientId: '1000000001',
        secrets: { apiKey: fakeKey, apiSecret: fakeSecret },
        apiKeyIssuedOn: istDayIso(),
        color: '#b45309', // the next colour in the palette after the two existing accounts
        notes: null,
        roles: {
          data: { enabled: true, kind: 'standby' },
          trading: { enabled: true },
          portfolio: { enabled: true },
        },
        trading: {
          mode: 'paper',
          riskProfileId: ids.standard,
          capitalSource: 'fixed',
          capitalAmount: 150000,
          styles: ['swing'],
          instruments: { intraday: [], swingBuy: ['EQ_CNC'], swingSell: [] },
          overrides: { riskPerTradePct: null, maxNewEntriesPerDay: null, maxOpenPositions: null, dailyLossPct: null },
        },
        portfolio: {
          includeInTotals: true,
          refreshMode: 'off',
          refreshDailyTimeIst: '16:00',
          firstImportDate: '2025-01-01',
        },
        data: { liveFeedBudget: 200 },
      },
    ]);
  });

  it('has no bucket choices: a calendar for the first import date, the totals checkbox and the auto refresh', async () => {
    renderPage();
    const dialog = await openWizard();
    await fillDhanApp(dialog);
    await next(dialog);
    await next(dialog);

    const section = await within(dialog).findByRole('region', { name: 'Portfolio settings' });
    expect(within(section).queryByLabelText(/bucket/i)).not.toBeInTheDocument();
    expect(within(section).queryByRole('button', { name: 'Add bucket' })).not.toBeInTheDocument();
    expect(within(section).queryByRole('combobox')).not.toBeInTheDocument();
    // No account yet: a calendar with the default, and a note that the first sync after connecting imports.
    const date = within(section).getByLabelText('First import date');
    expect(date).toHaveAttribute('type', 'date');
    expect(date).toHaveAttribute('min', '1990-01-01');
    expect(date).toHaveAttribute('max', istDayIso());
    expect(date).toHaveValue('2025-01-01');
    expect(within(section).getByText(/imports them with this date/)).toBeInTheDocument();
    expect(within(section).getByRole('checkbox', { name: 'Include in portfolio totals' })).toBeChecked();
    expect(within(section).getByRole('radio', { name: 'Off' })).toBeChecked();
  });

  it('blocks a future first import date on the way to the review', async () => {
    renderPage();
    const dialog = await openWizard();
    await fillDhanApp(dialog);
    await next(dialog);
    await next(dialog);

    fireEvent.change(await within(dialog).findByLabelText('First import date'), { target: { value: '2999-01-01' } });
    await next(dialog);

    expect(await within(dialog).findByText('The date cannot be in the future.')).toBeInTheDocument();
    expect(within(dialog).queryByText('Check the details, then create the account.')).not.toBeInTheDocument();
  });

  it('sends the chosen first import date and no bucket with the new account', async () => {
    const gate = createStepUpGate();
    const bodies: Record<string, unknown>[] = [];
    server.use(
      gate.handler,
      http.post('/api/accounts', async ({ request }) => {
        const denied = gate.require();
        if (denied) {
          return denied;
        }
        bodies.push((await request.json()) as Record<string, unknown>);
        return HttpResponse.json(created(), { status: 201 });
      }),
    );
    renderPage();
    const dialog = await openWizard();
    await fillDhanApp(dialog);
    await next(dialog);
    await userEvent.click(within(dialog).getByRole('checkbox', { name: 'Data' }));
    await next(dialog);

    fireEvent.change(await within(dialog).findByLabelText('First import date'), { target: { value: '2024-06-03' } });
    await next(dialog);
    expect(await within(dialog).findByText('Check the details, then create the account.')).toBeInTheDocument();
    expect(within(dialog).getByText('3 Jun 2024')).toBeInTheDocument();
    expect(within(dialog).queryByText(/bucket/i)).not.toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Create account' }));
    const passwordDialog = await screen.findByRole('dialog', { name: 'Confirm your password' });
    await userEvent.type(within(passwordDialog).getByLabelText(/Password/), 'correct-password');
    await userEvent.click(within(passwordDialog).getByRole('button', { name: 'Confirm' }));

    await waitFor(() => {
      expect(bodies).toHaveLength(1);
    });
    expect(bodies[0]?.portfolio).toEqual({
      includeInTotals: true,
      refreshMode: 'off',
      refreshDailyTimeIst: '16:00',
      firstImportDate: '2024-06-03',
    });
  });

  it('sends the chosen auto refresh with the new account and checks the daily time on the way', async () => {
    const gate = createStepUpGate();
    const bodies: Record<string, unknown>[] = [];
    server.use(
      gate.handler,
      http.post('/api/accounts', async ({ request }) => {
        const denied = gate.require();
        if (denied) {
          return denied;
        }
        bodies.push((await request.json()) as Record<string, unknown>);
        return HttpResponse.json(created(), { status: 201 });
      }),
    );
    renderPage();
    const dialog = await openWizard();
    await fillDhanApp(dialog);
    await next(dialog);
    await userEvent.click(within(dialog).getByRole('checkbox', { name: 'Data' }));
    await next(dialog);

    await userEvent.click(await within(dialog).findByRole('radio', { name: 'Daily at' }));
    await userEvent.clear(within(dialog).getByLabelText('Time of day (IST)'));
    await next(dialog);
    expect(await within(dialog).findByText('Enter a time as HH:MM, for example 16:00.')).toBeInTheDocument();
    await userEvent.type(within(dialog).getByLabelText('Time of day (IST)'), '09:30');
    await next(dialog);
    await userEvent.click(await within(dialog).findByRole('button', { name: 'Create account' }));
    const passwordDialog = await screen.findByRole('dialog', { name: 'Confirm your password' });
    await userEvent.type(within(passwordDialog).getByLabelText(/Password/), 'correct-password');
    await userEvent.click(within(passwordDialog).getByRole('button', { name: 'Confirm' }));

    await waitFor(() => {
      expect(bodies).toHaveLength(1);
    });
    expect(bodies[0]?.portfolio).toEqual({
      includeInTotals: true,
      refreshMode: 'daily',
      refreshDailyTimeIst: '09:30',
      firstImportDate: '2025-01-01',
    });
  });

  it('asks Dhan for a client id, both secrets and the day the key was generated', async () => {
    renderPage();
    const dialog = await openWizard();

    expect(within(dialog).getByLabelText(/^Client id/)).toBeRequired();
    await next(dialog);

    expect(await within(dialog).findByText("Required: 4 to 32 letters, digits, '_' or '-'.")).toBeInTheDocument();
    expect(within(dialog).getByLabelText(/^API key \(app_id\)/)).toHaveAccessibleDescription('Enter the API key.');
    expect(within(dialog).getByLabelText(/^API secret \(app_secret\)/)).toHaveAccessibleDescription(
      'Enter the API secret.',
    );
    expect(within(dialog).getByText(/static IP is needed only for placing orders/)).toBeInTheDocument();
  });

  it('masks secrets until shown, and never shows them in the review', async () => {
    renderPage();
    const dialog = await openWizard();

    const key = within(dialog).getByLabelText(/^API key \(app_id\)/);
    expect(key).toHaveAttribute('type', 'password');
    expect(key).toHaveAttribute('autocomplete', 'new-password'); // password managers must not offer to save it
    expect(key).toHaveValue('');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Show API key (app_id)' }));
    expect(key).toHaveAttribute('type', 'text');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Hide API key (app_id)' }));
    expect(key).toHaveAttribute('type', 'password');

    await reachReview(dialog);
    expect(within(dialog).getAllByText('•••• set')).toHaveLength(2);
    expect(within(dialog).getByText('1000••01')).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent(fakeKey);
    expect(document.body).not.toHaveTextContent(fakeSecret);
  });

  it('copies the redirect URL', async () => {
    const user = userEvent.setup();
    renderPage();
    const dialog = await openWizard();

    await user.click(await within(dialog).findByRole('button', { name: 'Copy redirect URL' }));

    expect(await within(dialog).findByRole('button', { name: 'Copied' })).toBeInTheDocument();
    expect(await navigator.clipboard.readText()).toBe(dhanRedirectUrl);
  });

  it('forgets typed secrets when the wizard is cancelled', async () => {
    renderPage();
    let dialog = await openWizard();
    await userEvent.type(within(dialog).getByLabelText(/^API key \(app_id\)/), fakeKey);
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog', { name: 'Add Dhan account' })).not.toBeInTheDocument();

    dialog = await openWizard();

    expect(within(dialog).getByLabelText(/^API key \(app_id\)/)).toHaveValue('');
  });

  it('needs at least one role and trading details before the review', async () => {
    renderPage();
    const dialog = await openWizard();
    await fillDhanApp(dialog);
    await next(dialog);

    await userEvent.click(within(dialog).getByRole('checkbox', { name: 'Portfolio' }));
    await next(dialog);
    expect(await within(dialog).findByText('Choose at least one role.')).toBeInTheDocument();

    await userEvent.click(within(dialog).getByRole('checkbox', { name: 'Trading' }));
    await next(dialog);
    await next(dialog); // capital and instruments are still empty
    expect(await within(dialog).findByText('Capital must be more than 0.')).toBeInTheDocument();
    expect(within(dialog).getByText('Choose at least one instrument for swing buys.')).toBeInTheDocument();
  });

  it('goes back without losing the answers', async () => {
    renderPage();
    const dialog = await openWizard();
    await fillDhanApp(dialog);
    const name = within(dialog).getByLabelText(/^Display name/);
    await userEvent.clear(name);
    await userEvent.type(name, 'My Dhan');
    await next(dialog);

    await back(dialog);
    expect(within(dialog).getByLabelText(/^Display name/)).toHaveValue('My Dhan');
    await back(dialog);
    expect(within(dialog).getByLabelText(/^API key \(app_id\)/)).toHaveValue(fakeKey);
  });

  it('jumps to the Dhan app step when the server rejects a secret', async () => {
    server.use(
      http.post('/api/accounts', () =>
        problemResponse(
          {
            title: 'Invalid',
            code: 'accounts.invalid',
            errors: {
              'secrets.apiKey': ['The API key is too long.'],
              'trading.riskProfileId': ['Unknown risk profile.'],
            },
          },
          400,
        ),
      ),
    );
    renderPage();
    const dialog = await openWizard();
    await reachReview(dialog);

    await userEvent.click(within(dialog).getByRole('button', { name: 'Create account' }));

    expect(await within(dialog).findByText('Step 1 of 5: Dhan app')).toBeInTheDocument();
    expect(within(dialog).getByLabelText(/^API key \(app_id\)/)).toHaveAccessibleDescription(
      'The API key is too long.',
    );
  });

  it('jumps to the step that owns a role-settings field the server rejected', async () => {
    server.use(
      http.post('/api/accounts', () =>
        problemResponse(
          {
            title: 'Invalid',
            code: 'accounts.invalid',
            errors: { 'trading.capitalAmount': ['Capital must be more than 0.'] },
          },
          400,
        ),
      ),
    );
    renderPage();
    const dialog = await openWizard();
    await reachReview(dialog);

    await userEvent.click(within(dialog).getByRole('button', { name: 'Create account' }));

    expect(await within(dialog).findByText('Step 4 of 5: Role settings')).toBeInTheDocument();
    expect(within(dialog).getByLabelText(/^Capital \(₹\)/)).toHaveAccessibleDescription('Capital must be more than 0.');
  });

  it('shows a conflict (e.g. a name already in use) on the review step', async () => {
    server.use(
      http.post('/api/accounts', () =>
        problemResponse({ title: 'An account with this name exists already.', code: 'accounts.conflict' }, 409),
      ),
    );
    renderPage();
    const dialog = await openWizard();
    await reachReview(dialog);

    await userEvent.click(within(dialog).getByRole('button', { name: 'Create account' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('exists already');
    expect(within(dialog).getByText('Step 5 of 5: Review')).toBeInTheDocument();
  });

  it('leaves everything unchanged when the owner cancels the password dialog', async () => {
    const gate = createStepUpGate();
    server.use(
      gate.handler,
      http.post('/api/accounts', () => gate.require() ?? HttpResponse.json(created(), { status: 201 })),
    );
    renderPage();
    const dialog = await openWizard();
    await reachReview(dialog);

    await userEvent.click(within(dialog).getByRole('button', { name: 'Create account' }));
    const passwordDialog = await screen.findByRole('dialog', { name: 'Confirm your password' });
    await userEvent.click(within(passwordDialog).getByRole('button', { name: 'Cancel' }));

    expect(await within(dialog).findByRole('button', { name: 'Create account' })).toBeEnabled();
    expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: 'Add Dhan account' })).toBeInTheDocument();
  });

  it('creates and connects with one password confirmation, then opens the Dhan sign-in', async () => {
    const assign = vi.spyOn(externalNavigation, 'assign').mockImplementation(() => undefined);
    const gate = createStepUpGate();
    const connects: string[] = [];
    server.use(
      gate.handler,
      http.post('/api/accounts', () => gate.require() ?? HttpResponse.json(created(), { status: 201 })),
      http.post('/api/accounts/:id/connect', ({ params }) => {
        const denied = gate.require();
        if (denied) {
          return denied;
        }
        connects.push(String(params.id));
        return HttpResponse.json({ authorizeUrl: dhanAuthorizeUrl, expiresAt: '2026-10-11T10:10:00Z' });
      }),
    );
    renderPage();
    const dialog = await openWizard();
    await reachReview(dialog);

    // Secondary to "Create account", which stays the main action.
    expect(within(dialog).getByRole('button', { name: 'Create account' })).toHaveAttribute('type', 'submit');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Create and connect' }));
    const passwordDialog = await screen.findByRole('dialog', { name: 'Confirm your password' });
    await userEvent.type(within(passwordDialog).getByLabelText(/Password/), 'correct-password');
    await userEvent.click(within(passwordDialog).getByRole('button', { name: 'Confirm' }));

    await waitFor(() => {
      expect(assign).toHaveBeenCalledWith(dhanAuthorizeUrl);
    });
    expect(gate.stepUpCalls).toBe(1);
    expect(connects).toEqual([newId]);
  });

  it('keeps the new account when connecting cannot start, and says why', async () => {
    const assign = vi.spyOn(externalNavigation, 'assign').mockImplementation(() => undefined);
    server.use(
      http.post('/api/accounts', () => HttpResponse.json(created(), { status: 201 })),
      http.post('/api/accounts/:id/connect', () =>
        problemResponse({ title: 'Dhan did not answer.', code: 'accounts.broker_error', status: 502 }, 502),
      ),
    );
    const { router } = renderPage();
    const dialog = await openWizard();
    await reachReview(dialog);

    await userEvent.click(within(dialog).getByRole('button', { name: 'Create and connect' }));

    expect(
      await screen.findByText('Dhan · Main was added, but connecting failed: Dhan did not answer.'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('dialog', { name: 'Add Dhan account' })).not.toBeInTheDocument();
    expect(router.state.location.search).toBe(`?account=${newId}`);
    expect(assign).not.toHaveBeenCalled();
  });
});
