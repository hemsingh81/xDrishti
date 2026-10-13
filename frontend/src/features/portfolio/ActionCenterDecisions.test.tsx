import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { buildActionItem, sampleActionItems } from '@/test/actionCenterFixtures';
import { releasePending } from '@/test/gate';
import { stubPhoneWidth } from '@/test/matchMedia';
import { actionCenterCard, actionItem, renderPortfolio, serveActionCenter } from './testing';

beforeEach(() => {
  window.sessionStorage.clear();
});
afterEach(() => {
  vi.unstubAllGlobals();
});

const tcsBelowCost = (card: HTMLElement) => actionItem(card, /^TCS Down 23.4/);
const snooze = (card: HTMLElement) =>
  userEvent.click(within(tcsBelowCost(card)).getByRole('button', { name: /^Snooze 7 days/ }));

describe('Action center: decisions', () => {
  it('snoozes an item: it leaves the list at once, the counts follow, and the toast says until when', async () => {
    const fake = serveActionCenter();
    renderPortfolio();
    const card = await actionCenterCard();
    await within(card).findByText('7 need a decision');

    await snooze(card);

    expect(await screen.findByText('Snoozed TCS until 20 Oct 2026.')).toBeInTheDocument();
    expect(within(card).queryByText('Down 23.4 % from cost')).not.toBeInTheDocument();
    expect(within(card).getByText('6 need a decision')).toBeInTheDocument();
    expect(within(card).getByRole('button', { name: 'Snoozed 1' })).toBeInTheDocument();
    // The High section had only that item.
    expect(within(card).queryByRole('heading', { level: 3, name: /^High/ })).not.toBeInTheDocument();
    expect(fake.calls.decisions).toEqual([{ id: sampleActionItems()[0]?.id, decision: 'snooze' }]);
  });

  it('shows the new state while the server has not answered, and puts the item back when it refuses', async () => {
    const fake = serveActionCenter();
    renderPortfolio();
    const card = await actionCenterCard();
    await within(card).findByText('7 need a decision');
    fake.failures.hold = true;

    await snooze(card);

    // Optimistic: gone from the list, counted as snoozed, before any answer.
    expect(within(card).queryByText('Down 23.4 % from cost')).not.toBeInTheDocument();
    expect(within(card).getByText('6 need a decision')).toBeInTheDocument();
    expect(within(card).getByRole('button', { name: 'Snoozed 1' })).toBeInTheDocument();

    await releasePending();

    expect(await screen.findByText('Could not snooze TCS. Nothing was changed.')).toBeInTheDocument();
    expect(await within(card).findByText('Down 23.4 % from cost')).toBeInTheDocument();
    expect(within(card).getByText('7 need a decision')).toBeInTheDocument();
    expect(within(card).getByRole('button', { name: 'Snoozed 0' })).toBeInTheDocument();
  });

  it('marks an item done and explains that it returns if the reason goes away and comes back', async () => {
    const fake = serveActionCenter();
    renderPortfolio();
    const card = await actionCenterCard();
    await within(card).findByText('7 need a decision');

    await userEvent.click(within(tcsBelowCost(card)).getByRole('button', { name: /^Done/ }));

    expect(await screen.findByText(/^Marked TCS done\./)).toBeInTheDocument();
    expect(within(card).getByRole('button', { name: 'Done 1' })).toBeInTheDocument();
    expect(fake.calls.decisions.at(-1)?.decision).toBe('done');
  });

  it('reopens a snoozed item from the Snoozed list', async () => {
    serveActionCenter({
      items: [buildActionItem({ state: 'snoozed', snoozedUntil: '2026-10-20' }), ...sampleActionItems().slice(1)],
    });
    renderPortfolio();
    const card = await actionCenterCard();
    await userEvent.click(await within(card).findByRole('button', { name: 'Snoozed 1' }));
    await within(card).findByText('Snoozed until 20 Oct 2026');

    await userEvent.click(within(tcsBelowCost(card)).getByRole('button', { name: /^Reopen/ }));

    expect(await screen.findByText('Reopened TCS.')).toBeInTheDocument();
    expect(within(card).queryByText('Snoozed until 20 Oct 2026')).not.toBeInTheDocument();
    expect(within(card).getByText('Nothing is snoozed.')).toBeInTheDocument();
    expect(within(card).getByRole('button', { name: 'Open 7' })).toBeInTheDocument();
  });

  it('reads the items again after a decision, to show what the server holds', async () => {
    const fake = serveActionCenter();
    renderPortfolio();
    const card = await actionCenterCard();
    await within(card).findByText('7 need a decision');
    const before = fake.calls.actionReads.length;

    await snooze(card);

    await vi.waitFor(() => {
      expect(fake.calls.actionReads.length).toBeGreaterThan(before);
    });
  });

  it.each([
    [409, 'TCS was changed meanwhile. The list was reloaded: look at it again.'],
    [404, 'TCS is no longer on the list: what it was about has changed. The list was reloaded.'],
    [500, 'The decision was refused.'],
  ])('puts the item back and says why when the answer is %i', async (status, message) => {
    const fake = serveActionCenter();
    renderPortfolio();
    const card = await actionCenterCard();
    await within(card).findByText('7 need a decision');
    fake.failures.decision = status;

    await snooze(card);

    expect(await screen.findByText(message)).toBeInTheDocument();
    expect(await within(card).findByText('Down 23.4 % from cost')).toBeInTheDocument();
    expect(within(card).getByText('7 need a decision')).toBeInTheDocument();
  });

  it('moves the keyboard to the next item when the focused button goes away', async () => {
    serveActionCenter();
    renderPortfolio();
    const card = await actionCenterCard();
    const button = within(tcsBelowCost(card)).getByRole('button', { name: /^Snooze 7 days/ });
    button.focus();

    await userEvent.keyboard('{Enter}');

    await screen.findByText('Snoozed TCS until 20 Oct 2026.');
    expect(within(actionItem(card, /^INFY Behind Nifty/)).getByRole('button', { name: 'Open INFY' })).toHaveFocus();
  });
});

describe('Action center: on a phone', () => {
  it('folds the buttons of an item into an Actions menu', async () => {
    stubPhoneWidth();
    const fake = serveActionCenter();
    renderPortfolio();
    const card = await actionCenterCard();
    const row = tcsBelowCost(card);
    expect(within(row).queryByRole('button', { name: /^Snooze/ })).not.toBeInTheDocument();

    const trigger = within(row).getByRole('button', { name: 'Actions for TCS' });
    expect(trigger).toHaveAttribute('aria-haspopup', 'menu');
    await userEvent.click(trigger);

    const menu = await screen.findByRole('menu');
    expect(
      within(menu)
        .getAllByRole('menuitem')
        .map((item) => item.textContent),
    ).toEqual(['Open', 'Set alert', 'Add to basket', 'Snooze 7 days', 'Done']);
    await userEvent.click(within(menu).getByRole('menuitem', { name: 'Snooze 7 days' }));

    expect(await screen.findByText('Snoozed TCS until 20 Oct 2026.')).toBeInTheDocument();
    expect(fake.calls.decisions).toHaveLength(1);
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
  });

  it('opens the holding from the menu and closes the menu with Escape', async () => {
    stubPhoneWidth();
    const { router } = renderPortfolio();
    serveActionCenter();
    const card = await actionCenterCard();
    const trigger = within(tcsBelowCost(card)).getByRole('button', { name: 'Actions for TCS' });

    await userEvent.click(trigger);
    await screen.findByRole('menu');
    await userEvent.keyboard('{Escape}');

    await vi.waitFor(() => {
      expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    });
    expect(trigger).toHaveFocus();
    expect(router.state.location.search).toBe('');
  });
});
