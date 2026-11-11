import { fireEvent, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildBasket } from '@/test/fixtures';
import { renderRoutes } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { SettingsPage } from './components/SettingsPage';

const basketId = '00000000-0000-4000-8000-0000000000b1';

const sizing = (over: Record<string, unknown> = {}) => ({
  volTargetEnabled: false,
  floor: 0.5,
  capacityEnabled: false,
  capacityPct: 0.005,
  minFit: 0.5,
  version: 0,
  notice: 'n',
  latestLoosening: null,
  history: [],
  ...over,
});
const sizingRow = (id: number, direction: string) => ({
  id,
  key: 'sizing.vol_target.floor',
  oldValue: '0.5',
  newValue: '0.7',
  direction,
  actor: 'owner:me@x.test',
  changedAt: '2026-11-10T06:00:00Z',
  hash: 'h',
});
const budgets = (over: Record<string, unknown> = {}) => ({ budgets: [], notice: 'n', history: [], ...over });
const basketSettings = (over: Record<string, unknown> = {}) => ({
  breadthSource: 'tracked',
  maxClusterRiskPct: null,
  clusterRho: 0.7,
  version: 0,
  notice: 'n',
  latestLoosening: null,
  history: [],
  ...over,
});
const looseningSetting = (over: Record<string, unknown> = {}) => ({
  autoDismiss: false,
  version: 0,
  hash: null,
  notice: 'OFF by default.',
  history: [],
  ...over,
});

function mount(overrides: { sizing?: unknown; budgets?: unknown; basket?: unknown; auto?: unknown } = {}) {
  server.use(
    http.get('/api/selection/sizing-settings', () => HttpResponse.json(overrides.sizing ?? sizing())),
    http.get('/api/selection/basket-risk-budgets', () => HttpResponse.json(overrides.budgets ?? budgets())),
    http.get('/api/selection/basket-settings', () => HttpResponse.json(overrides.basket ?? basketSettings())),
    http.get('/api/plans/loosening-settings', () => HttpResponse.json(overrides.auto ?? looseningSetting())),
    http.get('/api/baskets', () => HttpResponse.json([buildBasket({ id: basketId, name: 'Momentum' })])),
  );
  return renderRoutes([{ path: '/settings', element: <SettingsPage /> }], { initialPath: '/settings?tab=limits' });
}

const form = (name: string) => within(screen.getByRole('form', { name }));
const type = (formName: string, name: RegExp, value: string) => {
  fireEvent.change(form(formName).getByLabelText(name), { target: { value } });
};
const SIZING = 'Change the sizing settings';
const BUDGET = 'Change a basket budget';
const BASKET = 'Change the basket settings';
const AUTO = 'Change the automatic dismissal';

describe('Settings → Limits: the sizing settings', () => {
  it('shows the values in force and the history, newest first, with the direction', async () => {
    mount({ sizing: sizing({ version: 4, floor: 0.7, history: [sizingRow(4, 'loosen')] }) });

    expect(await screen.findByText(/Volatility scale/)).toHaveTextContent('floor 0.7');
    const table = screen.getByRole('table', { name: 'Sizing settings history' });
    expect(within(table).getByText('loosen')).toBeInTheDocument();
    expect(within(table).getByText('owner:me@x.test')).toBeInTheDocument();
  });

  it('shows an error when the settings cannot be read', async () => {
    server.use(http.get('/api/selection/sizing-settings', () => problemResponse({ title: 'Sizing is down.' }, 500)));
    mount();
    server.use(http.get('/api/selection/sizing-settings', () => problemResponse({ title: 'Sizing is down.' }, 500)));

    expect(await screen.findByText('Sizing is down.')).toBeInTheDocument();
  });

  it('sends the typed values with the password, clears it, and says the change loosens when the server stored it as loosen', async () => {
    let body: Record<string, unknown> = {};
    mount();
    server.use(
      http.put('/api/selection/sizing-settings', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(sizing({ version: 1, floor: 0.7, history: [sizingRow(1, 'loosen')] }));
      }),
    );
    await screen.findByText(/Volatility scale/);

    type(SIZING, /^Volatility floor/, '0.7');
    type(SIZING, /^Your password for this change/, 'pw');
    await userEvent.click(screen.getByRole('button', { name: 'Change the sizing settings' }));

    expect(
      await screen.findByText(/This loosens a limit: it will be shown on the next plan until dismissed/),
    ).toBeInTheDocument();
    expect(body).toMatchObject({ floor: 0.7, password: 'pw', volTargetEnabled: false });
    expect(form(SIZING).getByLabelText(/^Your password for this change/)).toHaveValue('');
  });

  it('says a tightening change was recorded without the loosening sentence', async () => {
    mount();
    server.use(
      http.put('/api/selection/sizing-settings', () =>
        HttpResponse.json(sizing({ version: 1, capacityPct: 0.003, history: [sizingRow(1, 'tighten')] })),
      ),
    );
    await screen.findByText(/Volatility scale/);

    type(SIZING, /^Capacity share/, '0.003');
    type(SIZING, /^Your password for this change/, 'pw');
    await userEvent.click(screen.getByRole('button', { name: 'Change the sizing settings' }));

    expect(await screen.findByText(/Changed; the new row is in the history/)).toBeInTheDocument();
    expect(screen.queryByText(/This loosens a limit/)).not.toBeInTheDocument();
  });

  it('refuses a value outside the range before sending anything and keeps the button off until the password is typed', async () => {
    mount();
    await screen.findByText(/Volatility scale/);

    type(SIZING, /^Volatility floor/, '2');
    type(SIZING, /^Your password for this change/, 'pw');

    expect(screen.getByRole('button', { name: 'Change the sizing settings' })).toBeDisabled();
  });

  it('says the settings were changed elsewhere on a 409 and reads them again', async () => {
    mount();
    server.use(
      http.put('/api/selection/sizing-settings', () =>
        problemResponse({ title: 'Stale.', code: 'selection.stale' }, 409),
      ),
    );
    await screen.findByText(/Volatility scale/);

    type(SIZING, /^Volatility floor/, '0.7');
    type(SIZING, /^Your password for this change/, 'pw');
    await userEvent.click(screen.getByRole('button', { name: 'Change the sizing settings' }));

    expect(await screen.findByText(/changed elsewhere/)).toBeInTheDocument();
  });

  it("shows the server's field messages on a 422", async () => {
    mount();
    server.use(
      http.put('/api/selection/sizing-settings', () =>
        problemResponse({ title: 'Invalid.', errors: { floor: ['The floor is 0.25 to 1.0.'] } }, 422),
      ),
    );
    await screen.findByText(/Volatility scale/);

    type(SIZING, /^Volatility floor/, '0.7');
    type(SIZING, /^Your password for this change/, 'pw');
    await userEvent.click(screen.getByRole('button', { name: 'Change the sizing settings' }));

    expect(await screen.findByText(/The floor is 0.25 to 1.0/)).toBeInTheDocument();
  });

  it('says the password was not accepted on a 401 and clears the field', async () => {
    mount();
    server.use(http.put('/api/selection/sizing-settings', () => problemResponse({ title: 'No.' }, 401)));
    await screen.findByText(/Volatility scale/);

    type(SIZING, /^Volatility floor/, '0.7');
    type(SIZING, /^Your password for this change/, 'bad');
    await userEvent.click(screen.getByRole('button', { name: 'Change the sizing settings' }));

    expect(await screen.findByText(/The password was not accepted/)).toBeInTheDocument();
    expect(form(SIZING).getByLabelText(/^Your password for this change/)).toHaveValue('');
  });
});

describe('Settings → Limits: the basket budgets', () => {
  it('lists the budgets in force by basket name, then sets one with the password', async () => {
    let call: { id: string; body: Record<string, unknown> } | null = null;
    mount({ budgets: budgets({ budgets: [{ basketId, budgetPct: 20 }] }) });
    server.use(
      http.put('/api/selection/basket-risk-budgets/:id', async ({ request, params }) => {
        call = { id: String(params.id), body: (await request.json()) as Record<string, unknown> };
        return HttpResponse.json(
          budgets({
            budgets: [{ basketId, budgetPct: 40 }],
            history: [
              {
                id: 2,
                basketId,
                budgetPct: 40,
                direction: 'loosen',
                actor: 'owner:me@x.test',
                changedAt: '2026-11-10T06:00:00Z',
                hash: 'h',
              },
            ],
          }),
        );
      }),
    );
    expect(await screen.findByText('Momentum: 20 percent of the open risk limit')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('combobox', { name: 'Basket' }));
    await userEvent.click(await screen.findByRole('option', { name: 'Momentum' }));
    type(BUDGET, /^Budget \(1 to 100/, '40');
    type(BUDGET, /^Your password for this change/, 'pw');
    await userEvent.click(screen.getByRole('button', { name: 'Change the budget' }));

    expect(await screen.findByText(/This loosens a limit/)).toBeInTheDocument();
    expect(call).toEqual({ id: basketId, body: { budgetPct: 40, password: 'pw' } });
  });

  it('shows a 409 as changed elsewhere', async () => {
    mount();
    server.use(http.put('/api/selection/basket-risk-budgets/:id', () => problemResponse({ title: 'Stale.' }, 409)));
    await screen.findByText('No basket has a risk budget.');

    await userEvent.click(screen.getByRole('combobox', { name: 'Basket' }));
    await userEvent.click(await screen.findByRole('option', { name: 'Momentum' }));
    type(BUDGET, /^Budget \(1 to 100/, '10');
    type(BUDGET, /^Your password for this change/, 'pw');
    await userEvent.click(screen.getByRole('button', { name: 'Change the budget' }));

    expect(await screen.findByText(/changed elsewhere/)).toBeInTheDocument();
  });
});

describe('Settings → Limits: the basket settings', () => {
  it('shows what is in force and sends only what changed with the password', async () => {
    let body: Record<string, unknown> = {};
    mount();
    server.use(
      http.put('/api/selection/basket-settings', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(
          basketSettings({
            version: 1,
            clusterRho: 0.8,
            history: [
              {
                id: 1,
                key: 'selection.cluster_rho',
                oldValue: '0.7',
                newValue: '0.8',
                direction: 'loosen',
                actor: 'owner:me@x.test',
                changedAt: '2026-11-10T06:00:00Z',
                hash: 'h',
              },
            ],
          }),
        );
      }),
    );
    expect(await screen.findByText(/The gate reads breadth from/)).toHaveTextContent(
      'cluster cap off; cluster cut 0.7',
    );

    type(BASKET, /^Cluster cut/, '0.8');
    type(BASKET, /^Your password for this change/, 'pw');
    await userEvent.click(screen.getByRole('button', { name: 'Change the basket settings' }));

    expect(await screen.findByText(/This loosens a limit/)).toBeInTheDocument();
    expect(body).toEqual({
      breadthSource: null,
      maxClusterRiskPct: null,
      clearCap: null,
      clusterRho: 0.8,
      password: 'pw',
    });
  });

  it("shows the server's message when a basket chosen as the source no longer exists", async () => {
    mount();
    server.use(
      http.put('/api/selection/basket-settings', () => problemResponse({ title: 'That basket does not exist.' }, 404)),
    );
    await screen.findByText(/The gate reads breadth from/);

    await userEvent.click(screen.getByRole('combobox', { name: 'Where the gate reads breadth' }));
    await userEvent.click(await screen.findByRole('option', { name: 'Basket Momentum' }));
    type(BASKET, /^Your password for this change/, 'pw');
    await userEvent.click(screen.getByRole('button', { name: 'Change the basket settings' }));

    expect(await screen.findByText(/That basket does not exist/)).toBeInTheDocument();
  });
});

describe('Settings → Limits: the automatic dismissal of the banner', () => {
  it('says it is off by default and switches it on with the version read and the password', async () => {
    let body: Record<string, unknown> = {};
    mount({ auto: looseningSetting({ version: 3 }) });
    server.use(
      http.put('/api/plans/loosening-settings', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(looseningSetting({ autoDismiss: true, version: 4 }));
      }),
    );
    expect(await screen.findByText(/Automatic dismissal is/)).toHaveTextContent('off');

    await userEvent.click(screen.getByRole('checkbox', { name: 'Hide a loosening automatically after one plan' }));
    type(AUTO, /^Your password for this change/, 'pw');
    await userEvent.click(screen.getByRole('button', { name: 'Change the automatic dismissal' }));

    expect(await screen.findByText(/Changed; the new row is in the history/)).toBeInTheDocument();
    expect(body).toEqual({ autoDismiss: true, expectedVersion: 3, password: 'pw' });
  });

  it('says a refusal for the missing password and shows a stale version as changed elsewhere', async () => {
    mount();
    server.use(
      http.put('/api/plans/loosening-settings', () => problemResponse({ title: 'Stale.', code: 'plans.stale' }, 409)),
    );
    await screen.findByText(/Automatic dismissal is/);

    await userEvent.click(screen.getByRole('checkbox', { name: 'Hide a loosening automatically after one plan' }));
    type(AUTO, /^Your password for this change/, 'pw');
    await userEvent.click(screen.getByRole('button', { name: 'Change the automatic dismissal' }));

    expect(await screen.findByText(/changed elsewhere/)).toBeInTheDocument();
  });
});
