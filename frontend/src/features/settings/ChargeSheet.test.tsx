import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createChargeSheetFake, sampleSheet } from '@/test/chargeSheetFake';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { SettingsPage } from './components/SettingsPage';

function renderTab(options: Parameters<typeof createChargeSheetFake>[0] = {}) {
  const fake = createChargeSheetFake(options);
  server.use(...fake.handlers);
  const view = renderRoutes([{ path: '/settings', element: <SettingsPage /> }], {
    initialPath: '/settings?tab=charge-sheet',
  });
  return { ...view, fake };
}

const reason = () => screen.findByRole('textbox', { name: /Why/ });
const password = () => screen.getByLabelText(/Your password/);
const button = (name: string | RegExp) => screen.getByRole('button', { name });

async function fillAct(text = 'checked against my contract notes', pass = 'correct-horse') {
  await userEvent.type(await reason(), text);
  await userEvent.type(password(), pass);
}

async function showSheet(id = 'dhan-2026') {
  await userEvent.type(await screen.findByRole('textbox', { name: 'Charge sheet id' }), id);
  await userEvent.click(button('Show sheet'));
  await screen.findByText('Dhan intraday and delivery', { exact: false });
}

describe('Settings: the charge sheet confirmation', () => {
  it('is a tab of Settings that loads on demand, kept in the URL', async () => {
    const fake = createChargeSheetFake();
    server.use(...fake.handlers);
    const { router } = renderRoutes([{ path: '/settings', element: <SettingsPage /> }], { initialPath: '/settings' });

    await userEvent.click(await screen.findByRole('tab', { name: 'Charge sheet' }));

    expect(router.state.location.search).toBe('?tab=charge-sheet');
    expect(await screen.findByRole('heading', { level: 2, name: 'Charge sheet confirmation' })).toBeInTheDocument();
  });

  it('says nothing is confirmed, that confirming is off by default, and what confirming means', async () => {
    renderTab();

    expect(await screen.findByText('Not confirmed')).toBeInTheDocument();
    expect(screen.getByText(/Not confirmed: no sheet is confirmed\. Every figure is gross\./)).toBeInTheDocument();
    expect(screen.getByText('Confirming is switched off (the default).')).toBeInTheDocument();
    expect(screen.getByText(/It places no order and changes no stored run or number\./)).toBeInTheDocument();
    expect(screen.getByText('No act yet.')).toBeInTheDocument();
    expect(button('Confirm this sheet')).toBeDisabled();
    expect(button('Revoke confirmation')).toBeDisabled();
  });

  it('keeps every act disabled until there is a reason and a password', async () => {
    renderTab();
    await screen.findByText('Not confirmed');

    expect(button('Allow confirming')).toBeDisabled();
    await userEvent.type(await reason(), 'I want to confirm my sheet');
    expect(button('Allow confirming')).toBeDisabled();
    await userEvent.type(password(), 'x');
    expect(button('Allow confirming')).toBeEnabled();
  });

  it('switches confirming on with a reason and the password, and clears the password after the act', async () => {
    const { fake } = renderTab();
    await screen.findByText('Not confirmed');
    await fillAct('I want to confirm my sheet');

    await userEvent.click(button('Allow confirming'));

    expect(await screen.findByText('Confirming is switched on.')).toBeInTheDocument();
    expect(fake.state.bodies).toEqual([
      { path: 'capability', body: { on: true, reason: 'I want to confirm my sheet', password: 'correct-horse' } },
    ]);
    await waitFor(() => {
      expect(password()).toHaveValue('');
    });
    expect(button('Stop allowing confirming')).toBeDisabled(); // a new act needs a new reason and a new password
    expect(screen.getByText('Confirming allowed')).toBeInTheDocument();
  });

  it('shows the sheet with the hash of its rates, and confirms exactly that sheet at exactly those rates', async () => {
    const { fake } = renderTab({ capabilityOn: true });
    await showSheet();
    expect(screen.getByText(sampleSheet.ratesSha256)).toBeInTheDocument();
    expect(screen.getByText('my October contract notes')).toBeInTheDocument();
    await fillAct();

    await userEvent.click(button('Confirm this sheet'));

    expect(await screen.findByRole('status')).toHaveTextContent(/Confirmed: charge sheet 'dhan-2026'/);
    expect(fake.state.bodies).toEqual([
      {
        path: 'confirm',
        body: {
          sheetId: 'dhan-2026',
          ratesSha256: sampleSheet.ratesSha256,
          reason: 'checked against my contract notes',
          password: 'correct-horse',
        },
      },
    ]);
    expect(screen.getByText('Confirmed')).toBeInTheDocument();
    expect(password()).toHaveValue('');
    expect(await reason()).toHaveValue('');
  });

  it('keeps Confirm disabled while confirming is off even with a sheet, a reason and a password', async () => {
    renderTab({ capabilityOn: false });
    await showSheet();
    await fillAct();

    expect(button('Confirm this sheet')).toBeDisabled();
  });

  it('says a wrong password was not accepted, clears it, writes nothing and does not sign the owner out', async () => {
    const { fake, router } = renderTab({ capabilityOn: true, wrongPassword: true });
    await showSheet();
    await fillAct('why', 'wrong');

    await userEvent.click(button('Confirm this sheet'));

    expect(await screen.findByText('The password was not accepted. Nothing was changed.')).toBeInTheDocument();
    expect(password()).toHaveValue('');
    expect(fake.state.confirmed).toBeNull();
    expect(router.state.location.pathname).toBe('/settings'); // not sent to the sign-in page
    expect(screen.getByRole('heading', { level: 2, name: 'Charge sheet confirmation' })).toBeInTheDocument();
  });

  it("shows the server's words when the rates changed meanwhile and writes nothing", async () => {
    const { fake } = renderTab({
      capabilityOn: true,
      refuse: {
        status: 409,
        code: 'chargesheet.rates_changed',
        title: 'The stored sheet has other rates than the ones you were shown; read it again before confirming.',
      },
    });
    await showSheet();
    await fillAct();

    await userEvent.click(button('Confirm this sheet'));

    expect(await screen.findByText(/has other rates than the ones you were shown/)).toBeInTheDocument();
    expect(fake.state.confirmed).toBeNull();
  });

  it('says Stale in words when the stored sheet has other rates now, and the confirmation can be revoked', async () => {
    const changed = { ...sampleSheet, ratesSha256: 'b'.repeat(64) };
    const { fake } = renderTab({
      sheet: changed,
      confirmed: { sheetId: 'dhan-2026', ratesSha256: sampleSheet.ratesSha256 },
    });

    expect(await screen.findByText('Stale')).toBeInTheDocument();
    expect(screen.getByText(/no longer has the rates you confirmed/)).toBeInTheDocument();
    await fillAct('wrong sheet');
    await userEvent.click(button('Revoke confirmation'));

    expect(await screen.findByText('Revoked')).toBeInTheDocument();
    expect(fake.state.bodies).toEqual([{ path: 'revoke', body: { reason: 'wrong sheet', password: 'correct-horse' } }]);
    expect(button('Revoke confirmation')).toBeDisabled();
  });

  it('says when no sheet of that name is stored', async () => {
    renderTab({ capabilityOn: true });
    await userEvent.type(await screen.findByRole('textbox', { name: 'Charge sheet id' }), 'nothing-here');
    await userEvent.click(button('Show sheet'));

    expect(await screen.findByText("No stored charge sheet is named 'nothing-here'.")).toBeInTheDocument();
    await fillAct();
    expect(button('Confirm this sheet')).toBeDisabled();
  });

  it('lists the acts and shows typed text as text, never as markup', async () => {
    renderTab({
      history: [
        {
          id: 1,
          kind: 'confirm',
          sheetId: 'dhan-2026',
          ratesSha256: null,
          actor: 'owner:example@invalid.test',
          reason: '<b>bold</b> <img src=x onerror=alert(1)>',
          at: '2026-10-26T06:00:00Z',
        },
      ],
    });

    const list = await screen.findByRole('list', { name: 'Charge sheet acts, newest first' });

    expect(within(list).getByText(/<b>bold<\/b>/)).toBeInTheDocument();
    expect(list.querySelector('b')).toBeNull();
    expect(list.querySelector('img')).toBeNull();
    expect(within(list).getByText(/26 Oct 2026, 11:30 IST by owner:example@invalid\.test/)).toBeInTheDocument();
  });

  it('has no control that sends a password anywhere but the three acts', async () => {
    const { fake } = renderTab();
    await screen.findByText('Not confirmed');

    await userEvent.type(password(), 'typed-but-not-sent');
    await userEvent.click(button('Show sheet'));

    expect(fake.state.bodies).toEqual([]);
  });
});
