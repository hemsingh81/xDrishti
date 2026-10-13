import { fireEvent, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildActionItem, sampleActionItems } from '@/test/actionCenterFixtures';
import { basketIds, holdingIds, ids, lotIds } from '@/test/fixtures';
import { pendingResponse } from '@/test/gate';
import { problemResponse, server } from '@/test/server';
import {
  actionCenterCard,
  actionItem,
  noHoldingsHandlers,
  renderPortfolio,
  serveActionCenter,
  twoPortfolioAccounts,
} from './testing';

beforeEach(() => {
  window.sessionStorage.clear();
});

const heading = (card: HTMLElement, name: string) => within(card).getByRole('heading', { level: 3, name });

/** The severity section under the heading `name`. */
function section(card: HTMLElement, name: string) {
  const found = heading(card, name).closest('section');
  if (!found) throw new Error(`No section under "${name}".`);
  return within(found);
}

describe('Action center: the card', () => {
  it('counts the items that need a decision and lists them by severity, most urgent first', async () => {
    serveActionCenter();
    renderPortfolio();

    const card = await actionCenterCard();
    expect(within(card).getByRole('heading', { level: 2, name: 'Action center' })).toBeInTheDocument();
    expect(within(card).getByText('7 need a decision')).toBeInTheDocument();
    expect(within(card).getByRole('button', { name: 'Open 7', pressed: true })).toBeInTheDocument();
    expect(within(card).getByRole('button', { name: 'Snoozed 0', pressed: false })).toBeInTheDocument();
    expect(within(card).getByRole('button', { name: 'Done 0', pressed: false })).toBeInTheDocument();

    const headings = within(card)
      .getAllByRole('heading', { level: 3 })
      .map((h) => h.textContent);
    expect(headings).toEqual(['High (1)', 'Medium (3)', 'Info (3)']);
    const high = section(card, 'High (1)').getAllByRole('listitem');
    expect(high).toHaveLength(1);
    expect(section(card, 'High (1)').getByText('Down 23.4 % from cost')).toBeInTheDocument();
    // The server's order inside a severity is kept: rule priority (lagging Nifty, overweight, sector).
    const medium = section(card, 'Medium (3)').getAllByRole('listitem');
    expect(
      medium.map((item) => within(item).getByText(/^(Lagging Nifty|Overweight|Sector concentration)$/).textContent),
    ).toEqual(['Lagging Nifty', 'Overweight', 'Sector concentration']);
    const info = section(card, 'Info (3)').getAllByRole('listitem');
    expect(info).toHaveLength(3);
  });

  it('shows for each item the severity in words, the rule, the holding, the sentences and the numbers', async () => {
    serveActionCenter();
    renderPortfolio();
    const card = await actionCenterCard();

    const tcs = actionItem(card, /^TCS Down 23.4 % from cost$/);
    expect(within(tcs).getByText('High')).toBeInTheDocument();
    expect(within(tcs).getByText('Below cost')).toBeInTheDocument();
    expect(within(tcs).getByRole('button', { name: 'TCS' })).toBeInTheDocument();
    expect(within(tcs).getByText('Test Dhan Main')).toBeInTheDocument();
    expect(
      within(tcs).getByText('TCS is worth ₹30,640 against ₹40,000 invested, a loss of ₹9,360.'),
    ).toBeInTheDocument();
    expect(within(tcs).getByText('Profit / loss').nextElementSibling).toHaveTextContent('−₹9,360 (−23.40%)');
    expect(within(tcs).getByText('Invested').nextElementSibling).toHaveTextContent('₹40,000');

    const sector = actionItem(card, /Energy is 41.2 % of the account/);
    expect(within(sector).getByText('Sector concentration')).toBeInTheDocument();
    expect(within(sector).getByText('Share of the account').nextElementSibling).toHaveTextContent('41.2%');
    expect(within(sector).getByText(/largest:/)).toHaveTextContent('largest: RELIANCE');

    const lagging = actionItem(card, /Behind Nifty 50 by 12.7 points/);
    expect(within(lagging).getByText('Gap').nextElementSibling).toHaveTextContent('−12.7 pp');
    expect(within(lagging).getByText('Nifty 50 XIRR').nextElementSibling).toHaveTextContent('+16.80%');

    const longTerm = actionItem(card, /One lot turns 12 months old soon/);
    expect(within(longTerm).getByText('10 shares bought 31 Oct 2025')).toBeInTheDocument();
    expect(within(longTerm).getByText(/12 months on 31 Oct 2026 \(in 18 days\), \+16.10%/)).toBeInTheDocument();
  });

  it('starts folded when nothing needs a decision, and opens on request to say so', async () => {
    serveActionCenter({ items: [] });
    renderPortfolio();

    const card = await actionCenterCard();
    expect(within(card).getByText('Nothing needs a decision')).toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: /^Open/ })).not.toBeInTheDocument();
    const toggle = within(card).getByRole('button', { name: 'Show the Action center' });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');

    await userEvent.click(toggle);

    expect(within(card).getByRole('button', { name: 'Hide the Action center' })).toHaveAttribute(
      'aria-expanded',
      'true',
    );
    expect(within(card).getByRole('button', { name: 'Open 0', pressed: true })).toBeInTheDocument();
    expect(within(card).getByText('Nothing needs a decision.')).toBeInTheDocument();
  });

  it('remembers that the owner folded it, for the session', async () => {
    serveActionCenter();
    const view = renderPortfolio();
    const card = await actionCenterCard();
    await userEvent.click(within(card).getByRole('button', { name: 'Hide the Action center' }));
    expect(within(card).queryByRole('heading', { level: 3 })).not.toBeInTheDocument();
    expect(within(card).getByText('7 need a decision')).toBeInTheDocument();

    view.unmount();
    renderPortfolio();

    const again = await actionCenterCard();
    expect(within(again).getByRole('button', { name: 'Show the Action center' })).toBeInTheDocument();
    expect(within(again).queryByRole('heading', { level: 3 })).not.toBeInTheDocument();
  });

  it('still works when the browser blocks session storage', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    serveActionCenter();
    renderPortfolio();
    const card = await actionCenterCard();

    await userEvent.click(within(card).getByRole('button', { name: 'Hide the Action center' }));

    expect(within(card).getByRole('button', { name: 'Show the Action center' })).toBeInTheDocument();
  });

  it('shows the card with placeholder rows while the items load, and no count yet', async () => {
    server.use(http.get('/api/portfolio/actions', () => pendingResponse()));
    renderPortfolio();

    const card = await screen.findByRole('region', { name: 'Action center' });
    expect(within(card).getByText('Loading the Action center')).toBeInTheDocument();
    expect(within(card).queryByText(/need a decision/)).not.toBeInTheDocument();
    expect(within(card).queryByRole('listitem')).not.toBeInTheDocument();
  });

  it('shows the problem with Retry when the items cannot be loaded, and recovers', async () => {
    const fake = serveActionCenter();
    fake.failures.actions = true;
    renderPortfolio();
    const card = await actionCenterCard();
    const alert = await within(card).findByRole('alert');
    expect(alert).toHaveTextContent('The service is not available.');

    fake.failures.actions = false;
    await userEvent.click(within(alert).getByRole('button', { name: 'Retry' }));

    // The same card shows placeholder rows while the retry is on its way, then the items.
    expect(await within(card).findByText('7 need a decision')).toBeInTheDocument();
    expect(within(card).queryByRole('alert')).not.toBeInTheDocument();
  });

  it('says what is wrong, without Retry, when the scope is refused (404)', async () => {
    server.use(
      http.get('/api/portfolio/actions', () =>
        problemResponse({ title: 'The account does not exist.', code: 'portfolio.not_found' }, 404),
      ),
    );
    renderPortfolio();

    const card = await actionCenterCard();
    const alert = await within(card).findByRole('alert');
    expect(alert).toHaveTextContent('The account does not exist.');
    expect(within(alert).queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument();
  });

  it('keeps the last items and warns when a refresh fails', async () => {
    const fake = serveActionCenter();
    renderPortfolio();
    const card = await actionCenterCard();
    await within(card).findByText('7 need a decision');

    fake.failures.actions = true;
    await userEvent.click(within(card).getByRole('button', { name: 'Snoozed 0' }));

    expect(await within(card).findByRole('alert')).toHaveTextContent('The service is not available.');
  });

  it('notes the rule it could not evaluate and the holdings without a price', async () => {
    serveActionCenter({ unavailableLagging: 3, noPrice: 2 });
    renderPortfolio();

    const card = await actionCenterCard();
    expect(
      within(card).getByText('1 rule not evaluated: Lagging Nifty needs Nifty 50 history (3 holdings affected).'),
    ).toBeInTheDocument();
    expect(
      within(card).getByText('2 holdings without a price are not checked by the price rules.'),
    ).toBeInTheDocument();
  });

  it('says nothing about coverage when every rule could be evaluated', async () => {
    serveActionCenter();
    renderPortfolio();

    const card = await actionCenterCard();
    await within(card).findByText('7 need a decision');
    expect(within(card).queryByText(/not evaluated/)).not.toBeInTheDocument();
    expect(within(card).queryByText(/without a price/)).not.toBeInTheDocument();
  });
});

describe('Action center: status filter and scope', () => {
  it('lists the snoozed items with the day they come back, and the done ones, each with Reopen', async () => {
    const items = [
      buildActionItem(),
      buildActionItem({
        id: `large_gain:${holdingIds.infy}`,
        rule: 'large_gain',
        severity: 'info',
        symbol: 'INFY',
        holdingId: holdingIds.infy,
        title: 'Up 31.2 % on cost',
        state: 'snoozed',
        snoozedUntil: '2026-10-20',
      }),
      buildActionItem({
        id: `not_in_basket:${holdingIds.tcs}`,
        rule: 'not_in_basket',
        severity: 'info',
        title: 'In no basket',
        state: 'done',
      }),
    ];
    const fake = serveActionCenter({ items });
    renderPortfolio();
    const card = await actionCenterCard();
    expect(await within(card).findByRole('button', { name: 'Snoozed 1' })).toBeInTheDocument();
    expect(within(card).getByRole('button', { name: 'Done 1' })).toBeInTheDocument();

    await userEvent.click(within(card).getByRole('button', { name: 'Snoozed 1' }));

    expect(await within(card).findByText('Snoozed until 20 Oct 2026')).toBeInTheDocument();
    const snoozed = actionItem(card, /INFY Up 31.2 % on cost/);
    expect(within(snoozed).getByRole('button', { name: /^Reopen/ })).toBeInTheDocument();
    expect(within(snoozed).queryByRole('button', { name: /^Snooze/ })).not.toBeInTheDocument();
    expect(within(snoozed).queryByRole('button', { name: /^Done/ })).not.toBeInTheDocument();
    expect(fake.calls.actionReads.at(-1)?.get('status')).toBe('snoozed');

    await userEvent.click(within(card).getByRole('button', { name: 'Done 1' }));

    expect(await within(card).findByText(/Marked done\./)).toBeInTheDocument();
    expect(within(card).getByRole('button', { name: /^Reopen/ })).toBeInTheDocument();
    expect(fake.calls.actionReads.at(-1)?.get('status')).toBe('done');
  });

  it('says so when a status has no items', async () => {
    serveActionCenter();
    renderPortfolio();
    const card = await actionCenterCard();

    await userEvent.click(await within(card).findByRole('button', { name: 'Snoozed 0' }));

    expect(await within(card).findByText('Nothing is snoozed.')).toBeInTheDocument();
  });

  it('follows the account of the scope bar', async () => {
    server.use(twoPortfolioAccounts());
    const fake = serveActionCenter();
    renderPortfolio(`?account=${ids.swing}`);

    const card = await actionCenterCard();
    await within(card).findByText(/need a decision|Nothing needs a decision/);
    expect(fake.calls.actionReads.every((params) => params.get('accountId') === ids.swing)).toBe(true);
    expect(fake.calls.actionReads.length).toBeGreaterThan(0);
  });

  it('is left out when there are no holdings at all', async () => {
    serveActionCenter();
    server.use(...noHoldingsHandlers());
    renderPortfolio();

    expect(await screen.findByText('No holdings yet')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Action center' })).not.toBeInTheDocument();
  });
});

describe('Action center: buttons', () => {
  it('opens the holding drawer from the symbol and from Open, with the holding in the URL', async () => {
    serveActionCenter();
    const { router } = renderPortfolio();
    const card = await actionCenterCard();

    await userEvent.click(within(actionItem(card, /^TCS Down 23.4/)).getByRole('button', { name: 'TCS' }));

    const drawer = await screen.findByLabelText('Holding details');
    expect(within(drawer).getByRole('heading', { name: 'TCS' })).toBeInTheDocument();
    expect(router.state.location.search).toBe(`?holding=${holdingIds.tcs}`);

    await userEvent.click(within(drawer).getByRole('button', { name: 'Close details' }));
    await vi.waitFor(() => {
      expect(router.state.location.search).toBe('');
      expect(screen.queryByLabelText('Holding details')).not.toBeInTheDocument();
    });
    await userEvent.click(within(actionItem(card, /^TCS In no basket/)).getByRole('button', { name: /^Open/ }));
    expect(await screen.findByLabelText('Holding details')).toBeInTheDocument();
    expect(router.state.location.search).toBe(`?holding=${holdingIds.tcs}`);
  });

  it('switches to the sector view of the account from the sector item', async () => {
    serveActionCenter();
    const { router } = renderPortfolio();
    const card = await actionCenterCard();

    await userEvent.click(within(actionItem(card, /Energy is 41.2 %/)).getByRole('button', { name: /^Open/ }));

    await vi.waitFor(() => {
      const params = new URLSearchParams(router.state.location.search);
      expect(params.get('groupBy')).toBe('sector');
      expect(params.get('account')).toBe(ids.main);
    });
    expect(await screen.findByRole('heading', { level: 2, name: 'Holdings by sector' })).toBeInTheDocument();
  });

  it('opens the largest holding of the sector from its link', async () => {
    serveActionCenter();
    const { router } = renderPortfolio();
    const card = await actionCenterCard();

    await userEvent.click(within(actionItem(card, /Energy is 41.2 %/)).getByRole('button', { name: 'RELIANCE' }));

    expect(await screen.findByLabelText('Holding details')).toBeInTheDocument();
    expect(router.state.location.search).toBe(`?holding=${holdingIds.reliance}`);
  });

  it('offers Set alert and Add to basket only for an item with an instrument', async () => {
    serveActionCenter();
    renderPortfolio();
    const card = await actionCenterCard();

    const tcs = actionItem(card, /^TCS Down 23.4/);
    expect(
      within(tcs)
        .getAllByRole('button')
        .map((b) => b.getAttribute('aria-label') ?? b.textContent),
    ).toEqual(['TCS', 'Open TCS', 'Set alert TCS', 'Add to basket TCS', 'Snooze 7 days TCS', 'Done TCS']);
    const sector = actionItem(card, /Energy is 41.2 %/);
    expect(within(sector).queryByRole('button', { name: /^Set alert/ })).not.toBeInTheDocument();
    expect(within(sector).queryByRole('button', { name: /^Add to basket/ })).not.toBeInTheDocument();
  });

  it('hides Set alert and Add to basket for a holding that is not in the master list', async () => {
    serveActionCenter({
      items: [buildActionItem({ instrumentId: null, symbol: 'SMALLCO', holdingId: holdingIds.smallco })],
    });
    renderPortfolio();
    const card = await actionCenterCard();

    const row = actionItem(card, /SMALLCO/);
    expect(within(row).getByRole('button', { name: /^Open/ })).toBeInTheDocument();
    expect(within(row).getByRole('button', { name: /^Snooze/ })).toBeInTheDocument();
    expect(within(row).queryByRole('button', { name: /^Set alert/ })).not.toBeInTheDocument();
    expect(within(row).queryByRole('button', { name: /^Add to basket/ })).not.toBeInTheDocument();
  });

  it('adds the instrument to the chosen basket and says what happened', async () => {
    serveActionCenter();
    const posts: { id: string; body: unknown }[] = [];
    server.use(
      http.post('/api/baskets/:id/members', async ({ params, request }) => {
        posts.push({ id: String(params.id), body: await request.json() });
        return HttpResponse.json({ requested: 1, added: 1, alreadyMembers: 0, newlyTracked: 0, memberCount: 1 });
      }),
    );
    renderPortfolio();
    const card = await actionCenterCard();

    await userEvent.click(
      within(actionItem(card, /^TCS In no basket/)).getByRole('button', { name: /^Add to basket/ }),
    );

    const dialog = within(await screen.findByRole('dialog', { name: 'Add TCS to baskets' }));
    await userEvent.click(await dialog.findByRole('checkbox', { name: /MyChoice/ }));
    await userEvent.click(dialog.getByRole('button', { name: 'Add to 1 basket' }));

    expect(await screen.findByText('Added TCS to MyChoice')).toBeInTheDocument();
    expect(posts).toEqual([{ id: basketIds.myChoice, body: { instrumentIds: [expect.any(String)], note: null } }]);
    expect(screen.queryByRole('dialog', { name: 'Add TCS to baskets' })).not.toBeInTheDocument();
  });

  it('reads the items again after the basket changed, since the item may be gone', async () => {
    const fake = serveActionCenter();
    server.use(
      http.post('/api/baskets/:id/members', () =>
        HttpResponse.json({ requested: 1, added: 1, alreadyMembers: 0, newlyTracked: 0, memberCount: 1 }),
      ),
    );
    renderPortfolio();
    const card = await actionCenterCard();
    await within(card).findByText('7 need a decision');
    const before = fake.calls.actionReads.length;

    await userEvent.click(
      within(actionItem(card, /^TCS In no basket/)).getByRole('button', { name: /^Add to basket/ }),
    );
    const dialog = within(await screen.findByRole('dialog', { name: 'Add TCS to baskets' }));
    await userEvent.click(await dialog.findByRole('checkbox', { name: /MyChoice/ }));
    await userEvent.click(dialog.getByRole('button', { name: 'Add to 1 basket' }));

    await vi.waitFor(() => {
      expect(fake.calls.actionReads.length).toBeGreaterThan(before);
    });
  });

  it('opens the alert dialog for the holding of the item', async () => {
    serveActionCenter();
    renderPortfolio();
    const card = await actionCenterCard();

    await userEvent.click(within(actionItem(card, /^TCS Down 23.4/)).getByRole('button', { name: /^Set alert/ }));

    const dialog = await screen.findByRole('dialog', { name: 'Set price alert' });
    expect(within(dialog).getByText('TCS')).toBeInTheDocument();
    expect(await within(dialog).findByText('Last price ₹3,880.00')).toBeInTheDocument();
  });

  it('opens the Alerts panel from the header', async () => {
    const fake = serveActionCenter();
    const { router } = renderPortfolio();
    const card = await actionCenterCard();

    await userEvent.click(within(card).getByRole('button', { name: 'Alerts' }));

    expect(await screen.findByRole('dialog', { name: 'Price alerts' })).toBeInTheDocument();
    expect(router.state.location.search).toBe('?alerts=active');
    expect(fake.calls.alertReads.length).toBeGreaterThan(0);
  });
});

describe('Action center: unknown data', () => {
  it('shows a lot without a cost as free shares instead of a percentage', async () => {
    serveActionCenter({
      items: [
        buildActionItem({
          id: `long_term_soon:${holdingIds.reliance}`,
          rule: 'long_term_soon',
          severity: 'info',
          title: 'One lot turns 12 months old soon',
          numbers: {
            lots: [{ acquiredOn: '2025-10-31', qty: 1, longTermOn: '2026-10-31', daysLeft: 1, gainPct: null }],
          },
        }),
      ],
    });
    renderPortfolio();

    const card = await actionCenterCard();
    expect(within(card).getByText('1 share bought 31 Oct 2025')).toBeInTheDocument();
    expect(within(card).getByText('12 months on 31 Oct 2026 (in 1 day), free shares (no cost)')).toBeInTheDocument();
  });

  it('shows a rule or severity this version does not know instead of hiding the item', async () => {
    serveActionCenter({
      items: [
        buildActionItem({ id: 'future_rule:x', rule: 'future_rule', severity: 'urgent', title: 'A new kind of item' }),
      ],
    });
    renderPortfolio();

    const card = await actionCenterCard();
    expect(within(card).getByText('future_rule')).toBeInTheDocument();
    expect(within(card).getByRole('heading', { level: 3, name: 'Info (1)' })).toBeInTheDocument();
    expect(within(card).getByText('A new kind of item')).toBeInTheDocument();
  });

  it('keeps the full list reachable: every sample item is a list item', async () => {
    serveActionCenter();
    renderPortfolio();
    const card = await actionCenterCard();

    expect(within(card).getAllByRole('listitem', { name: /./ })).toHaveLength(sampleActionItems().length);
  });
});

describe('Action center: arriving, looking up and keeping in step', () => {
  it('unfolds, scrolls to and focuses the card when the bell sends the owner there, once', async () => {
    window.sessionStorage.setItem('xd.portfolio.actions.collapsed', '1');
    serveActionCenter();
    const { router } = renderPortfolio('?focus=actions');

    const card = await actionCenterCard();
    await within(card).findByText('7 need a decision');
    expect(within(card).getByRole('button', { name: 'Hide the Action center' })).toBeInTheDocument();
    expect(within(card).getByRole('heading', { level: 2, name: 'Action center' })).toHaveFocus();
    await vi.waitFor(() => {
      expect(router.state.location.search).toBe('');
    });
  });

  it('shows the holding in the drawer even when the list does not hold it (it is looked up by its symbol)', async () => {
    serveActionCenter();
    renderPortfolio('?q=RELIANCE');
    const card = await actionCenterCard();
    await screen.findByRole('gridcell', { name: 'RELIANCE' });
    expect(screen.queryByRole('gridcell', { name: 'TCS' })).not.toBeInTheDocument();

    await userEvent.click(within(actionItem(card, /^TCS Down 23.4/)).getByRole('button', { name: 'TCS' }));

    const drawer = await screen.findByLabelText('Holding details');
    expect(await within(drawer).findByText('Tata Consultancy Services')).toBeInTheDocument();
    expect(within(drawer).getByText('Quantity', { selector: 'dt' }).nextElementSibling).toHaveTextContent('10');
  });

  it('reads the items again when a lot date is corrected, since the numbers may change', async () => {
    const fake = serveActionCenter();
    server.use(
      http.put('/api/holdings/:id/lots/:lotId', () =>
        HttpResponse.json({
          id: lotIds.first,
          acquiredOn: '2024-02-10',
        }),
      ),
    );
    renderPortfolio(`?holding=${holdingIds.infy}`);
    const drawer = await screen.findByLabelText('Holding details');
    await within(drawer).findByRole('table', { name: 'Lots' });
    await vi.waitFor(() => {
      expect(fake.calls.actionReads.length).toBeGreaterThan(0);
    });
    const before = fake.calls.actionReads.length;

    await userEvent.click(
      within(drawer).getByRole('button', { name: 'Edit the date of the lot bought on 1 Jan 2025' }),
    );
    const date = within(drawer).getByLabelText('Acquired on, lot of 1 Jan 2025');
    fireEvent.change(date, { target: { value: '2024-02-10' } });
    await userEvent.click(within(drawer).getByRole('button', { name: 'Save' }));

    await vi.waitFor(() => {
      expect(fake.calls.actionReads.length).toBeGreaterThan(before);
    });
  });
});
