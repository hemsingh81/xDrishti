import { fireEvent, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http } from 'msw';
import { buildActionList, buildCustomisedThresholds, defaultThresholds } from '@/test/actionCenterFixtures';
import { createActionCenterFake } from '@/test/actionCenterFake';
import { pendingResponse } from '@/test/gate';
import { renderRoutes } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { SettingsPage } from './components/SettingsPage';

const renderTab = (path = '/settings?tab=actions') =>
  renderRoutes([{ path: '/settings', element: <SettingsPage /> }], { initialPath: path });

function renderThresholds(options: Parameters<typeof createActionCenterFake>[0] = {}) {
  const fake = createActionCenterFake(options);
  server.use(...fake.handlers);
  return { ...renderTab(), fake };
}

const field = (name: RegExp | string) => screen.findByRole('textbox', { name });
const type = (input: HTMLElement, value: string) => {
  fireEvent.change(input, { target: { value } });
};
const save = () => screen.getByRole('button', { name: 'Save thresholds' });
const restore = () => screen.getByRole('button', { name: 'Restore defaults' });

describe('Settings: Action center thresholds', () => {
  it('is a tab of Settings, kept in the URL', async () => {
    server.use(...createActionCenterFake().handlers);
    const { router } = renderTab('/settings');

    await userEvent.click(await screen.findByRole('tab', { name: 'Action center' }));

    expect(router.state.location.search).toBe('?tab=actions');
    expect(await screen.findByRole('heading', { level: 2, name: 'Action center thresholds' })).toBeInTheDocument();
  });

  it('shows the ten thresholds with their units and ranges, and says they are the defaults', async () => {
    renderThresholds();

    expect(await field('Gain on cost (%)')).toHaveValue('25');
    expect(screen.getByText('1–1,000')).toBeInTheDocument();
    expect(screen.getAllByRole('textbox')).toHaveLength(10);
    expect(await screen.findByText('Defaults')).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Positions in the account' })).toHaveValue('3');
    expect(screen.getByText('2–50, whole number')).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Turns 12 months old within (days)' })).toHaveValue('30');
    // Nothing to save and nothing to restore.
    expect(save()).toBeDisabled();
    expect(restore()).toBeDisabled();
  });

  it('says it is customised, by whom and when', async () => {
    renderThresholds({ thresholds: buildCustomisedThresholds() });

    expect(await screen.findByText('Customised')).toBeInTheDocument();
    expect(screen.getByText(/^Changed by example@invalid\.test on 12 Oct 2026, .* IST$/)).toBeInTheDocument();
    expect(await field('Gain on cost (%)')).toHaveValue('40');
    expect(restore()).toBeEnabled();
  });

  it('saves a changed value (all ten are sent), says so, and has the Action center read again', async () => {
    const { fake, client } = renderThresholds();
    const key = ['portfolio', 'actions', 'list', {}];
    client.setQueryData(key, buildActionList([]));
    type(await field('Gain on cost (%)'), '40');
    expect(save()).toBeEnabled();

    await userEvent.click(save());

    expect(
      await screen.findByText('Action center thresholds saved. The Action center uses them from now on.'),
    ).toBeInTheDocument();
    expect(fake.calls.thresholdSaves).toEqual([{ ...defaultThresholds, gainPct: 40 }]);
    expect(await screen.findByText('Customised')).toBeInTheDocument();
    expect(save()).toBeDisabled();
    expect(client.getQueryState(key)?.isInvalidated).toBe(true);
  });

  it.each([
    ['Loss from cost (%)', '0', 'Use a value from 1 to 90.'],
    ['Loss from cost (%)', '91', 'Use a value from 1 to 90.'],
    ['Loss from cost (%)', 'abc', 'Enter a number from 1 to 90.'],
    ['Loss from cost (%)', '', 'Enter a number from 1 to 90.'],
    ['Loss from cost (%)', '12.345', 'Use at most 2 decimals.'],
    ['Loss from cost, high severity (%)', '10', 'Must be greater than the loss threshold (10 %).'],
    ['Gain on cost (%)', '1001', 'Use a value from 1 to 1,000.'],
    ['Times an equal share (×)', '11', 'Use a value from 1 to 10.'],
    ['Positions in the account', '2.5', 'Use a whole number from 2 to 50.'],
    ['Positions in the account', '1', 'Use a value from 2 to 50.'],
    ['Sector share of the account (%)', '4', 'Use a value from 5 to 100.'],
    ['Behind Nifty 50 by (pp)', '101', 'Use a value from 1 to 100.'],
    ['Held for more than (days)', '29', 'Use a value from 30 to 3,650.'],
    ['Turns 12 months old within (days)', '366', 'Use a value from 1 to 365.'],
  ])('refuses %s = "%s" before Save: %s', async (label, value, message) => {
    const { fake } = renderThresholds();
    const input = await field(label);

    type(input, value);

    expect(await screen.findByText(message)).toBeInTheDocument();
    expect(input).toBeInvalid();
    expect(save()).toBeDisabled();
    expect(fake.calls.thresholdSaves).toEqual([]);
  });

  it('accepts a high-severity loss once it is above the loss threshold', async () => {
    renderThresholds();
    const high = await field('Loss from cost, high severity (%)');
    type(high, '10');
    expect(await screen.findByText('Must be greater than the loss threshold (10 %).')).toBeInTheDocument();

    type(high, '10.5');

    expect(screen.queryByText(/Must be greater/)).not.toBeInTheDocument();
    expect(save()).toBeEnabled();
  });

  it('shows the API’s message at the field it names, and clears it when the field is edited', async () => {
    renderThresholds();
    server.use(
      http.put('/api/portfolio/action-thresholds', () =>
        problemResponse(
          { title: 'Not valid.', code: 'portfolio.invalid', errors: { gainPct: ['The gain must be above the loss.'] } },
          400,
        ),
      ),
    );
    const gain = await field('Gain on cost (%)');
    type(gain, '30');

    await userEvent.click(save());

    expect(await screen.findByText('The gain must be above the loss.')).toBeInTheDocument();
    expect(gain).toBeInvalid();
    await vi.waitFor(() => {
      expect(gain).toHaveFocus();
    });
    type(gain, '31');
    expect(screen.queryByText('The gain must be above the loss.')).not.toBeInTheDocument();
  });

  it('says so when the save fails for another reason, and keeps the values typed', async () => {
    renderThresholds();
    server.use(
      http.put('/api/portfolio/action-thresholds', () =>
        problemResponse({ title: 'The service is not available.' }, 503),
      ),
    );
    const gain = await field('Gain on cost (%)');
    type(gain, '30');

    await userEvent.click(save());

    expect(await screen.findByRole('alert')).toHaveTextContent('The service is not available.');
    expect(gain).toHaveValue('30');
  });

  it('restores the defaults and says so', async () => {
    const { fake } = renderThresholds({ thresholds: buildCustomisedThresholds() });
    await screen.findByText('Customised');

    await userEvent.click(restore());

    expect(await screen.findByText('Action center thresholds restored to the defaults.')).toBeInTheDocument();
    expect(fake.calls.thresholdResets).toBe(1);
    expect(await screen.findByText('Defaults')).toBeInTheDocument();
    expect(await field('Gain on cost (%)')).toHaveValue('25');
    expect(restore()).toBeDisabled();
  });

  it('puts the defaults back in the form without a request when only the form was edited', async () => {
    const { fake } = renderThresholds();
    const gain = await field('Gain on cost (%)');
    type(gain, '30');
    expect(restore()).toBeEnabled();

    await userEvent.click(restore());

    expect(gain).toHaveValue('25');
    expect(fake.calls.thresholdResets).toBe(0);
    expect(save()).toBeDisabled();
  });

  it('shows a progress indicator while loading', async () => {
    server.use(http.get('/api/portfolio/action-thresholds', () => pendingResponse()));
    renderTab();

    expect(await screen.findByText('Loading thresholds')).toBeInTheDocument();
  });

  it('shows the problem with Retry when the thresholds cannot be loaded', async () => {
    const fake = createActionCenterFake();
    fake.failures.thresholds = true;
    server.use(...fake.handlers);
    renderTab();
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('The service is not available.');

    fake.failures.thresholds = false;
    await userEvent.click(within(alert).getByRole('button', { name: 'Retry' }));

    expect(await field('Gain on cost (%)')).toHaveValue('25');
  });
});
