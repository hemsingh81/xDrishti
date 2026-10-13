import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildActionItem, sampleActionItems } from '@/test/actionCenterFixtures';
import { buildLot, ids, lotIds } from '@/test/fixtures';
import { pendingResponse, releasePending, releaseStep } from '@/test/gate';
import { stubPhoneWidth } from '@/test/matchMedia';
import { server } from '@/test/server';
import { actionCenterCard, actionItem, renderPortfolio, serveActionCenter, twoPortfolioAccounts } from './testing';

beforeEach(() => {
  window.sessionStorage.clear();
});
afterEach(() => {
  vi.unstubAllGlobals();
  Reflect.deleteProperty(window.HTMLElement.prototype, 'scrollIntoView');
});

/** Holds the answer for one status (or account) of the Action center; every other read goes on to the fake. */
const holdActions = (match: (params: URLSearchParams) => boolean) => {
  server.use(
    http.get('/api/portfolio/actions', ({ request }) =>
      match(new URL(request.url).searchParams) ? pendingResponse() : undefined,
    ),
  );
};

describe('Action center: a list is never another list', () => {
  it('shows placeholder rows, not the open items, while the Snoozed list loads, and keeps the scope counts', async () => {
    serveActionCenter();
    renderPortfolio();
    const card = await actionCenterCard();
    expect(within(card).getByText('Down 23.4 % from cost')).toBeInTheDocument();
    holdActions((params) => params.get('status') === 'snoozed');

    await userEvent.click(within(card).getByRole('button', { name: 'Snoozed 0' }));

    expect(await within(card).findByText('Loading the Action center')).toBeInTheDocument();
    expect(within(card).queryByText('Down 23.4 % from cost')).not.toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: /^Snooze 7 days/ })).not.toBeInTheDocument();
    expect(within(card).queryByRole('heading', { level: 3 })).not.toBeInTheDocument();
    // The counts are the scope's, whatever the status: they stay.
    expect(within(card).getByText('7 need a decision')).toBeInTheDocument();
    expect(within(card).getByRole('button', { name: 'Snoozed 0', pressed: true })).toBeInTheDocument();
  });

  it('shows nothing of the old scope, not even its count, while the list of a new scope loads', async () => {
    server.use(twoPortfolioAccounts());
    serveActionCenter();
    const view = renderPortfolio(`?account=${ids.main}`);
    const card = await actionCenterCard();
    expect(within(card).getByText('7 need a decision')).toBeInTheDocument();
    expect(within(card).getByText('For Test Dhan Main')).toBeInTheDocument();
    holdActions((params) => params.get('accountId') === ids.swing);

    await act(async () => {
      await view.router.navigate(`/portfolio?account=${ids.swing}`);
    });

    expect(await within(card).findByText('Loading the Action center')).toBeInTheDocument();
    expect(within(card).queryByText(/need a decision|Nothing needs a decision/)).not.toBeInTheDocument();
    expect(within(card).queryByText('Down 23.4 % from cost')).not.toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: /^Snooze 7 days/ })).not.toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: /^Open \d/ })).not.toBeInTheDocument();
    expect(within(card).getByText('For Test Dhan Swing')).toBeInTheDocument();
  });

  it('does not fold or unfold on the counts of another scope', async () => {
    server.use(twoPortfolioAccounts());
    serveActionCenter({ items: sampleActionItems().map((item) => ({ ...item, accountId: ids.main })) });
    const view = renderPortfolio(`?account=${ids.main}`);
    const card = await actionCenterCard();
    holdActions((params) => params.get('accountId') === ids.swing);

    await act(async () => {
      await view.router.navigate(`/portfolio?account=${ids.swing}`);
    });
    await within(card).findByText('Loading the Action center');

    // Nothing is known of the new scope yet: the card is not folded by the old scope's "7".
    expect(within(card).getByRole('button', { name: 'Hide the Action center' })).toBeInTheDocument();
  });
});

describe('Action center: long lists', () => {
  const many = (count: number) =>
    Array.from({ length: count }, (_, n) =>
      buildActionItem({
        id: `not_in_basket:00000000-0000-4000-8000-0000000014${String(n).padStart(3, '0')}`,
        rule: 'not_in_basket',
        severity: 'info',
        symbol: `SYM${String(n)}`,
        title: `In no basket ${String(n)}`,
        suggestions: ['open', 'add_to_basket', 'snooze', 'done'],
      }),
    );

  it('draws 50 items per severity on a phone, one Actions button each, and the rest with Show more', async () => {
    stubPhoneWidth();
    serveActionCenter({ items: [...sampleActionItems().slice(0, 2), ...many(120)] });
    renderPortfolio();
    const card = await actionCenterCard();

    const info = () => {
      const section = within(card).getByRole('heading', { level: 3, name: /^Info/ }).closest('section');
      if (!section) throw new Error('No Info section.');
      return within(section);
    };
    expect(within(card).getByRole('heading', { level: 3, name: 'Info (120)' })).toBeInTheDocument();
    expect(info().getAllByRole('listitem')).toHaveLength(50);
    expect(info().getAllByRole('button', { name: /^Actions for/ })).toHaveLength(50);
    expect(info().getByText('Showing 50 of 120')).toBeInTheDocument();
    // The other severities are not held back by it.
    expect(within(card).getByRole('heading', { level: 3, name: 'High (1)' })).toBeInTheDocument();

    await userEvent.click(info().getByRole('button', { name: 'Show 50 more' }));
    expect(info().getAllByRole('listitem')).toHaveLength(100);
    await userEvent.click(info().getByRole('button', { name: 'Show 20 more' }));
    expect(info().getAllByRole('listitem')).toHaveLength(120);
    expect(info().queryByRole('button', { name: /Show .* more/ })).not.toBeInTheDocument();
  });

  it('does not offer Show more for a short list', async () => {
    serveActionCenter();
    renderPortfolio();
    const card = await actionCenterCard();

    expect(within(card).queryByRole('button', { name: /Show .* more/ })).not.toBeInTheDocument();
  });
});

describe('Action center: where the keyboard goes', () => {
  const snoozeTcs = (card: HTMLElement) =>
    userEvent.click(within(actionItem(card, /^TCS Down 23.4/)).getByRole('button', { name: /^Snooze 7 days/ }));

  it('moves to the first button of the next item when an item is decided', async () => {
    serveActionCenter();
    renderPortfolio();
    const card = await actionCenterCard();

    await snoozeTcs(card);

    await screen.findByText('Snoozed TCS until 20 Oct 2026.');
    expect(within(actionItem(card, /^INFY Behind Nifty/)).getByRole('button', { name: 'Open INFY' })).toHaveFocus();
  });

  it('moves to the previous item when the last one is decided, and to the card title when none is left', async () => {
    serveActionCenter({ items: sampleActionItems().slice(0, 2) });
    renderPortfolio();
    const card = await actionCenterCard();
    const last = within(actionItem(card, /^INFY Behind Nifty/));

    await userEvent.click(last.getByRole('button', { name: /^Done/ }));
    await screen.findByText(/^Marked INFY done/);
    expect(within(actionItem(card, /^TCS Down 23.4/)).getByRole('button', { name: 'Open TCS' })).toHaveFocus();

    await userEvent.click(within(actionItem(card, /^TCS Down 23.4/)).getByRole('button', { name: /^Done/ }));
    await screen.findByText(/^Marked TCS done/);
    expect(within(card).getByRole('heading', { level: 2, name: 'Action center' })).toHaveFocus();
  });

  it('does not scroll the page to move the keyboard', async () => {
    serveActionCenter();
    renderPortfolio();
    const card = await actionCenterCard();
    const focus = vi.spyOn(window.HTMLElement.prototype, 'focus');

    await snoozeTcs(card);

    await screen.findByText('Snoozed TCS until 20 Oct 2026.');
    expect(focus).toHaveBeenCalledWith({ preventScroll: true });
  });

  it('returns to the same button when the server refuses and the item comes back', async () => {
    const fake = serveActionCenter();
    renderPortfolio();
    const card = await actionCenterCard();
    fake.failures.hold = true;

    await snoozeTcs(card);
    expect(within(card).queryByText('Down 23.4 % from cost')).not.toBeInTheDocument();
    await releasePending();

    expect(await within(card).findByText('Down 23.4 % from cost')).toBeInTheDocument();
    await waitFor(() => {
      expect(within(actionItem(card, /^TCS Down 23.4/)).getByRole('button', { name: /^Snooze 7 days/ })).toHaveFocus();
    });
  });
});

describe('Action center: quick decisions and slow reloads', () => {
  it('does not reload the lists between two quick decisions, only after the last answer', async () => {
    const fake = serveActionCenter();
    fake.failures.stepwise = true;
    renderPortfolio();
    const card = await actionCenterCard();
    const reads = fake.calls.actionReads.length;

    await userEvent.click(within(actionItem(card, /^TCS Down 23.4/)).getByRole('button', { name: /^Snooze 7 days/ }));
    await userEvent.click(within(actionItem(card, /^INFY Behind Nifty/)).getByRole('button', { name: /^Done/ }));
    expect(within(card).getByText('5 need a decision')).toBeInTheDocument();
    expect(fake.calls.decisions.map((d) => d.decision)).toEqual(['snooze', 'done']);

    await releaseStep();
    await screen.findByText('Snoozed TCS until 20 Oct 2026.');
    // The first answer is in, the second is not: no reload that would bring back a list without the second decision.
    expect(fake.calls.actionReads.length).toBe(reads);
    expect(within(card).getByText('5 need a decision')).toBeInTheDocument();

    await releaseStep();
    await screen.findByText(/^Marked INFY done/);
    await waitFor(() => {
      expect(fake.calls.actionReads.length).toBeGreaterThan(reads);
    });
    expect(await within(card).findByText('5 need a decision')).toBeInTheDocument();
  });

  it('tells the owner as soon as the server has answered, without waiting for the reload', async () => {
    serveActionCenter();
    renderPortfolio();
    const card = await actionCenterCard();
    // From now on the lists answer slowly (the reload after a decision is held).
    holdActions(() => true);

    await userEvent.click(within(actionItem(card, /^TCS Down 23.4/)).getByRole('button', { name: /^Snooze 7 days/ }));

    expect(await screen.findByText('Snoozed TCS until 20 Oct 2026.')).toBeInTheDocument();
    expect(within(card).queryByText('Down 23.4 % from cost')).not.toBeInTheDocument();
  });
});

describe('Action center: the sector view', () => {
  it('says what it did, shows the account in the card and scrolls to the holdings', async () => {
    const scrolled = vi.fn();
    window.HTMLElement.prototype.scrollIntoView = scrolled;
    serveActionCenter();
    renderPortfolio();
    const card = await actionCenterCard();

    await userEvent.click(within(actionItem(card, /Energy is 41.2 %/)).getByRole('button', { name: /^Open/ }));

    expect(
      await within(card).findByText('Showing the holdings of Test Dhan Main by sector, below.'),
    ).toBeInTheDocument();
    expect(within(card).getByRole('status')).toHaveTextContent(
      'Showing the holdings of Test Dhan Main by sector, below.',
    );
    expect(within(card).getByText('For Test Dhan Main')).toBeInTheDocument();
    await waitFor(() => {
      expect(scrolled).toHaveBeenCalled();
    });
  });
});

describe('Action center: lookups follow the holdings', () => {
  it('reads the holding of the drawer again after a lot date changed', async () => {
    serveActionCenter();
    let lookups = 0;
    server.use(
      http.get('/api/holdings', ({ request }) => {
        if (new URL(request.url).searchParams.get('q') === 'INFY') lookups += 1;
        return undefined;
      }),
      http.put('/api/holdings/:id/lots/:lotId', () =>
        HttpResponse.json(buildLot({ id: lotIds.first, acquiredOn: '2024-02-10' })),
      ),
    );
    renderPortfolio('?q=RELIANCE');
    const card = await actionCenterCard();
    await userEvent.click(within(actionItem(card, /^INFY Behind Nifty/)).getByRole('button', { name: 'INFY' }));
    const drawer = await screen.findByLabelText('Holding details');
    await within(drawer).findByRole('table', { name: 'Lots' });
    expect(lookups).toBe(1);

    await userEvent.click(
      within(drawer).getByRole('button', { name: 'Edit the date of the lot bought on 1 Jan 2025' }),
    );
    fireEvent.change(within(drawer).getByLabelText('Acquired on, lot of 1 Jan 2025'), {
      target: { value: '2024-02-10' },
    });
    await userEvent.click(within(drawer).getByRole('button', { name: 'Save' }));

    await waitFor(() => {
      expect(lookups).toBeGreaterThan(1);
    });
  });
});
