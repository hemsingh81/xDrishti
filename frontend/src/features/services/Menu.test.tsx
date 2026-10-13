import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createServicesFake } from '@/test/servicesFake';
import { buildMasterService, buildServiceView, serviceIds } from '@/test/servicesFixtures';
import { chooseAction, openActions, renderServices, serviceCard } from './testing';

const eod = 'End-of-day data run';

describe('Services: the actions menu of a card', () => {
  it('shows one primary button, Run now, and one overflow button; the other actions are in the menu', async () => {
    renderServices();

    const card = await serviceCard(eod);
    const buttons = within(card)
      .getAllByRole('button')
      .map((b) => b.getAttribute('aria-label'));
    expect(buttons).toEqual([`Run now: ${eod}`, `Actions for ${eod}`]);
    for (const direct of ['Configure', 'Pause', 'History']) {
      expect(within(card).queryByRole('button', { name: new RegExp(`^${direct}`) })).not.toBeInTheDocument();
    }
  });

  it('opens a menu with Configure, Pause and History, and says it is one', async () => {
    renderServices();
    const button = await screen.findByRole('button', { name: `Actions for ${eod}` });
    expect(button).toHaveAttribute('aria-haspopup', 'menu');
    expect(button).toHaveAttribute('aria-expanded', 'false');

    const menu = await openActions(eod);

    expect(button).toHaveAttribute('aria-expanded', 'true');
    expect(
      within(menu)
        .getAllByRole('menuitem')
        .map((i) => i.textContent),
    ).toEqual(['Configure', 'Pause', 'History']);
  });

  it('works with the keyboard: Enter opens it on the first item, arrows move, Escape closes and returns to the button', async () => {
    renderServices();
    const button = await screen.findByRole('button', { name: `Actions for ${eod}` });
    button.focus();

    await userEvent.keyboard('{Enter}');
    const menu = await screen.findByRole('menu', { name: `Actions for ${eod}` });
    await waitFor(() => {
      expect(within(menu).getByRole('menuitem', { name: 'Configure' })).toHaveFocus();
    });
    await userEvent.keyboard('{ArrowDown}');
    expect(within(menu).getByRole('menuitem', { name: 'Pause' })).toHaveFocus();
    await userEvent.keyboard('{Escape}');

    await waitFor(() => {
      expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    });
    expect(button).toHaveFocus();
    expect(button).toHaveAttribute('aria-expanded', 'false');
  });

  it('chooses an item with the keyboard: Enter on History opens the drawer', async () => {
    renderServices();
    const button = await screen.findByRole('button', { name: `Actions for ${eod}` });
    button.focus();

    await userEvent.keyboard('{Enter}');
    await screen.findByRole('menu');
    await userEvent.keyboard('{ArrowDown}{ArrowDown}{Enter}');

    expect(await screen.findByRole('dialog', { name: 'Run history' })).toBeInTheDocument();
  });

  it('gives the focus back to the menu button when the dialog it opened is closed', async () => {
    renderServices();
    const button = await screen.findByRole('button', { name: `Actions for ${eod}` });

    await chooseAction(eod, 'Configure');
    const dialog = await screen.findByRole('dialog', { name: `Configure — ${eod}` });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    await waitFor(() => {
      expect(button).toHaveFocus();
    });
  });

  it('keeps Run now and Resume available on a paused service', async () => {
    renderServices({
      fake: createServicesFake({
        services: [buildServiceView({ enabled: false, status: 'paused', nextRun: null }), buildMasterService()],
      }),
    });

    const card = await serviceCard(eod);
    expect(within(card).getByRole('button', { name: `Run now: ${eod}` })).toBeEnabled();
    const menu = await openActions(eod);
    expect(within(menu).getByRole('menuitem', { name: 'Resume' })).toBeEnabled();
    expect(within(menu).getByRole('menuitem', { name: 'Configure' })).toBeEnabled();
    expect(within(menu).getByRole('menuitem', { name: 'History' })).toBeEnabled();
  });

  it('turns Run now off while the service runs, but still offers Configure and History', async () => {
    const fake = createServicesFake({ autoAdvance: false });
    fake.startScheduledRun(serviceIds.master);
    renderServices({ fake });

    const card = await serviceCard('Instrument master refresh');
    expect(within(card).getByRole('button', { name: 'Run now: Instrument master refresh' })).toBeDisabled();
    const menu = await openActions('Instrument master refresh');
    expect(within(menu).getByRole('menuitem', { name: 'Configure' })).toBeEnabled();
    expect(within(menu).getByRole('menuitem', { name: 'History' })).toBeEnabled();
  });

  it('has Settings… and Manage in Accounts in the menu of the account sync, and no Run now', async () => {
    renderServices();

    const menu = await openActions('Account & portfolio sync');

    expect(
      within(menu)
        .getAllByRole('menuitem')
        .map((i) => i.textContent),
    ).toEqual(['Settings…', 'Manage in Accounts']);
    expect(within(menu).getByRole('menuitem', { name: 'Manage in Accounts' })).toHaveAttribute('href', '/accounts');
  });
});
