import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { dragWithKeyboard, announced, stubLayout } from './dndTestkit';
import { basketIds, membersGrid, portfolioSeeds, renderBaskets, symbolsShown } from './testing';

const dhan = basketIds.dhanPortfolio;
const empty = basketIds.zerodhaPortfolio;
const toast = () => screen.findByRole('alert');
const rowBox = (symbol: string) => screen.getByRole('checkbox', { name: `Select ${symbol}` });
const portfolioCard = (name: string) => screen.getByRole('button', { name: new RegExp(`^${name}`) });

/** Renders the screen with the Dhan portfolio basket selected (holding TCS and RELIANCE). */
async function openPortfolio(search = `?basket=${dhan}`) {
  const view = renderBaskets({ search, portfolio: portfolioSeeds });
  await screen.findByRole('heading', { level: 2, name: 'Dhan · Main · Portfolio' });
  await membersGrid();
  return view;
}

/** Makes every media query match: a phone-sized screen for the theme's `down('md')`. */
function stubPhone() {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: true,
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    addListener: () => undefined,
    removeListener: () => undefined,
    dispatchEvent: () => false,
    onchange: null,
  }));
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('Baskets page: the Portfolio group', () => {
  it('lists the portfolio baskets first in the same row of cards, as `portfolio` with a lock, without group headings', async () => {
    renderBaskets({ portfolio: portfolioSeeds });

    const cards = within(await screen.findByRole('list', { name: 'Baskets' })).getAllByRole('listitem');
    expect(cards).toHaveLength(5);
    expect(cards[0]).toHaveTextContent('Dhan · Main · Portfolio');
    expect(cards[0]).toHaveTextContent('portfolio');
    expect(cards[0]).toHaveTextContent('2 instruments');
    expect(cards[0]?.querySelector('[data-testid="LockOutlinedIcon"]')).not.toBeNull();
    expect(cards[1]).toHaveTextContent('Retirement bucket');
    expect(cards[1]).toHaveTextContent('0 instruments');
    expect(cards.slice(2).map((card) => card.textContent)).toEqual([
      expect.stringContaining('MyChoice'),
      expect.stringContaining('MyLongTerm'),
      expect.stringContaining('Best stocks'),
    ]);
    expect(cards[2]).toHaveTextContent('manual');
    expect(screen.queryByRole('heading', { name: 'Portfolio' })).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Your baskets' })).not.toBeInTheDocument();
  });

  it('still opens the first of the owner’s baskets, not the portfolio group above them', async () => {
    renderBaskets({ portfolio: portfolioSeeds });

    expect(await screen.findByRole('heading', { level: 2, name: 'MyChoice' })).toBeInTheDocument();
    expect(await membersGrid()).toBeInTheDocument();
  });

  it('gives only the owner’s baskets a grip to reorder them', async () => {
    renderBaskets({ portfolio: portfolioSeeds });
    await membersGrid();

    expect(screen.getAllByRole('button', { name: /^Reorder / }).map((b) => b.getAttribute('aria-label'))).toEqual([
      'Reorder MyChoice',
      'Reorder MyLongTerm',
      'Reorder Best stocks',
    ]);
  });

  it('selects a portfolio basket from its card and keeps the choice in the URL', async () => {
    const { router } = renderBaskets({ portfolio: portfolioSeeds });
    await membersGrid();

    await userEvent.click(portfolioCard('Dhan · Main · Portfolio'));

    expect(await screen.findByRole('heading', { level: 2, name: 'Dhan · Main · Portfolio' })).toBeInTheDocument();
    expect(router.state.location.search).toBe(`?basket=${dhan}`);
    expect(portfolioCard('Dhan · Main · Portfolio')).toHaveAttribute('aria-current', 'true');
    await waitFor(() => {
      expect(symbolsShown()).toEqual(['NSE:TCS', 'NSE:RELIANCE']);
    });

    await userEvent.click(screen.getByRole('button', { name: /^Best stocks/ }));
    expect(router.state.location.search).toBe(`?basket=${basketIds.bestStocks}`);
  });

  it('opens the portfolio basket named in the URL', async () => {
    await openPortfolio();

    expect(portfolioCard('Dhan · Main · Portfolio')).toHaveAttribute('aria-current', 'true');
  });

  it('shows only the portfolio cards, and an invitation to create a basket, when the owner has none', async () => {
    renderBaskets({ baskets: [], members: {}, portfolio: portfolioSeeds });

    expect(await screen.findByRole('heading', { level: 2, name: 'Dhan · Main · Portfolio' })).toBeInTheDocument();
    expect(screen.getByText('No baskets of your own yet — create one with New basket.')).toBeInTheDocument();
    expect(within(screen.getByRole('list', { name: 'Baskets' })).getAllByRole('listitem')).toHaveLength(2);
  });
});

describe('Baskets page: a portfolio basket is read only', () => {
  it('shows the lock, the account, the note and the holdings with quantity and account', async () => {
    await openPortfolio();

    expect(screen.getByText('Portfolio · read only')).toBeInTheDocument();
    expect(
      screen.getByText(
        "Portfolio — follows the account's holdings; read only. Copy instruments to one of your baskets.",
      ),
    ).toBeInTheDocument();
    const grid = screen.getByRole('grid', { name: 'Basket members' });
    expect(
      within(grid)
        .getAllByRole('columnheader')
        .map((h) => h.textContent),
    ).toEqual(expect.arrayContaining(['Quantity', 'Account']));
    expect(within(grid).queryByRole('columnheader', { name: 'Added' })).not.toBeInTheDocument();
    const tcs = within(grid).getByRole('row', { name: /NSE:TCS/ });
    expect(within(tcs).getByText('10')).toBeInTheDocument();
    expect(within(tcs).getByText('Dhan · Main')).toBeInTheDocument();
    expect(within(grid).getByRole('row', { name: /NSE:RELIANCE/ })).toHaveTextContent('25');
  });

  it('has no archive, edit, history, as-of, add or remove controls, and cannot be reordered', async () => {
    await openPortfolio();

    for (const name of ['Archive', 'Edit', 'History', 'Add instruments', 'Add', 'Move to…', 'Remove']) {
      expect(screen.queryByRole('button', { name })).not.toBeInTheDocument();
    }
    expect(screen.queryByRole('textbox', { name: 'Add instrument' })).not.toBeInTheDocument();
    expect(screen.queryByLabelText('View as of')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Remove NSE:/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Copy or move / })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Reorder Dhan/ })).not.toBeInTheDocument();
    // The selection toolbar is there only while something is selected, and then offers Copy to… alone.
    expect(screen.queryByRole('button', { name: 'Copy to…' })).not.toBeInTheDocument();
    await userEvent.click(rowBox('NSE:TCS'));
    expect(screen.getByRole('button', { name: 'Copy to…' })).toBeEnabled();
    expect(screen.queryByRole('button', { name: /^Move to/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Remove/ })).not.toBeInTheDocument();
  });

  it('does not send the look-back date of the URL for a portfolio basket', async () => {
    const { fake } = await openPortfolio(`?basket=${dhan}&asOf=2026-10-01`);

    expect(fake.calls.members.filter((c) => c.id === dhan).map((c) => c.asOf)).toEqual([null]);
    expect(screen.queryByText(/Showing the members as they were/)).not.toBeInTheDocument();
    expect(symbolsShown()).toEqual(['NSE:TCS', 'NSE:RELIANCE']);
  });

  it('keeps the look-back date for the owner’s baskets', async () => {
    const { fake } = renderBaskets({
      search: `?basket=${basketIds.myChoice}&asOf=2026-10-01`,
      portfolio: portfolioSeeds,
    });

    expect(await screen.findByText(/Showing the members as they were/)).toBeInTheDocument();
    expect(fake.calls.members[0]).toEqual({ id: basketIds.myChoice, asOf: '2026-10-01' });
  });

  it('shows the empty state of a bucket without holdings', async () => {
    renderBaskets({ search: `?basket=${empty}`, portfolio: portfolioSeeds });

    expect(
      await screen.findByText("No holdings in this bucket yet — they arrive when the account's portfolio is imported"),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Add instruments' })).not.toBeInTheDocument();
  });
});

describe('Baskets page: copying out of a portfolio basket', () => {
  it('offers Copy to only the owner’s baskets from the row menu, never Move, and Undo takes the copy out', async () => {
    const { fake } = await openPortfolio();

    await userEvent.click(screen.getByRole('button', { name: 'Copy NSE:TCS' }));
    const menu = await screen.findByRole('menu', { name: 'Copy NSE:TCS' });
    expect(
      within(menu)
        .getAllByRole('menuitem')
        .map((item) => item.getAttribute('aria-label')),
    ).toEqual(['Copy to MyChoice', 'Copy to MyLongTerm', 'Copy to Best stocks']);
    await userEvent.click(within(menu).getByRole('menuitem', { name: 'Copy to Best stocks' }));

    expect(await toast()).toHaveTextContent('Copied NSE:TCS to Best stocks (1 was not tracked and now is)');
    expect(fake.symbolsIn(basketIds.bestStocks)).toEqual(['NSE:TCS']);
    expect(fake.holdingsIn(dhan)).toEqual(['NSE:TCS', 'NSE:RELIANCE']);
    expect(fake.calls.transfer).toEqual([expect.objectContaining({ fromBasketId: dhan, mode: 'copy' })]);

    await userEvent.click(screen.getByRole('button', { name: 'Undo' }));

    expect(await screen.findByText(/Copy undone: removed NSE:TCS from Best stocks/)).toBeInTheDocument();
    expect(fake.symbolsIn(basketIds.bestStocks)).toEqual([]);
    expect(symbolsShown()).toEqual(['NSE:TCS', 'NSE:RELIANCE']);
  });

  it('copies the selection from the toolbar menu, which has no Move to', async () => {
    const { fake } = await openPortfolio();
    await userEvent.click(rowBox('NSE:TCS'));
    await userEvent.click(rowBox('NSE:RELIANCE'));

    await userEvent.click(screen.getByRole('button', { name: 'Copy to…' }));
    const menu = await screen.findByRole('menu', { name: 'Copy to another basket from Dhan · Main · Portfolio' });
    expect(within(menu).queryByRole('menuitem', { name: /^Move to/ })).not.toBeInTheDocument();
    await userEvent.click(within(menu).getByRole('menuitem', { name: 'Copy to MyLongTerm' }));

    expect(await toast()).toHaveTextContent('Copied 2 instruments to MyLongTerm');
    expect(fake.symbolsIn(basketIds.myLongTerm)).toEqual(['NSE:TCS', 'NSE:RELIANCE']);
  });

  it('copies a row dragged onto a card with the keyboard, and Alt does not turn it into a move', async () => {
    stubLayout();
    const { fake } = await openPortfolio();

    await dragWithKeyboard('Drag NSE:TCS to copy', ['{ArrowUp}'], ['Alt']);

    expect(announced()).toBe('Dropped NSE:TCS on basket Best stocks.');
    expect(await toast()).toHaveTextContent(/^Copied NSE:TCS to Best stocks/);
    expect(fake.calls.transfer).toEqual([
      expect.objectContaining({ fromBasketId: dhan, toBasketId: basketIds.bestStocks, mode: 'copy' }),
    ]);
    expect(screen.getByRole('button', { name: 'Undo' })).toBeInTheDocument();
    expect(symbolsShown()).toEqual(['NSE:TCS', 'NSE:RELIANCE']);
  });

  it('copies with the mouse, saying "copy" even with Alt held, and highlights only the owner’s cards', async () => {
    stubLayout();
    const { fake } = await openPortfolio();
    const grip = screen.getByRole('button', { name: 'Drag NSE:TCS to copy' });

    fireEvent.mouseDown(grip, { button: 0, clientX: 320, clientY: 220 });
    fireEvent.mouseMove(document, { clientX: 400, clientY: 150 });
    fireEvent.mouseMove(document, { clientX: 370, clientY: 140, altKey: true });
    const hot = document.querySelectorAll('[data-drop-target]');
    expect(hot).toHaveLength(1);
    expect(hot[0]).toHaveAttribute('data-drop-target', 'copy');
    expect(hot[0]).toHaveTextContent('MyLongTerm');
    expect(hot[0]).toHaveTextContent('Drop to copy here');
    fireEvent.mouseUp(document, { clientX: 370, clientY: 140, altKey: true });

    expect(await toast()).toHaveTextContent(/^Copied NSE:TCS to MyLongTerm/);
    expect(fake.calls.transfer).toEqual([expect.objectContaining({ mode: 'copy' })]);
  });

  it('does not reorder the rows when one is dropped on another', async () => {
    stubLayout();
    const { fake } = await openPortfolio();
    const grip = screen.getByRole('button', { name: 'Drag NSE:TCS to copy' });

    fireEvent.mouseDown(grip, { button: 0, clientX: 500, clientY: 220 });
    fireEvent.mouseMove(document, { clientX: 500, clientY: 250 });
    fireEvent.mouseMove(document, { clientX: 500, clientY: 260 });
    expect(document.querySelector('[data-drop]')).toBeNull();
    fireEvent.mouseUp(document, { clientX: 500, clientY: 260 });

    await waitFor(() => {
      expect(document.querySelector('[data-drag]')).toBeNull();
    });
    expect(fake.calls.reorderMembers).toHaveLength(0);
    expect(fake.calls.transfer).toHaveLength(0);
    expect(symbolsShown()).toEqual(['NSE:TCS', 'NSE:RELIANCE']);
  });
});

describe('Baskets page: portfolio baskets are never a target', () => {
  it('lists only the owner’s baskets in the row menu of a manual basket', async () => {
    renderBaskets({ portfolio: portfolioSeeds });
    await membersGrid();

    await userEvent.click(screen.getByRole('button', { name: 'Copy or move NSE:TCS' }));

    const menu = await screen.findByRole('menu', { name: 'Copy or move NSE:TCS' });
    expect(
      within(menu)
        .getAllByRole('menuitem')
        .map((item) => item.getAttribute('aria-label')),
    ).toEqual(['Copy to MyLongTerm', 'Copy to Best stocks', 'Move to MyLongTerm', 'Move to Best stocks']);
  });

  it('lists only the owner’s baskets in the toolbar menus of a manual basket', async () => {
    renderBaskets({ portfolio: portfolioSeeds });
    await membersGrid();
    await userEvent.click(rowBox('NSE:TCS'));

    await userEvent.click(screen.getByRole('button', { name: 'Move to…' }));

    const menu = await screen.findByRole('menu');
    expect(
      within(menu)
        .getAllByRole('menuitem')
        .map((item) => item.getAttribute('aria-label')),
    ).toEqual(['Move to MyLongTerm', 'Move to Best stocks']);
  });

  it('ignores a drop on a portfolio card', async () => {
    stubLayout();
    const { fake } = renderBaskets({ portfolio: portfolioSeeds });
    await membersGrid();
    const grip = screen.getByRole('button', { name: 'Drag NSE:TCS' });

    // The stubbed portfolio cards sit at y 1000 and below; a card that is a drop target would be found there.
    fireEvent.mouseDown(grip, { button: 0, clientX: 320, clientY: 220 });
    fireEvent.mouseMove(document, { clientX: 140, clientY: 1040 });
    fireEvent.mouseMove(document, { clientX: 140, clientY: 1050 });
    expect(document.querySelector('[data-drop-target]')).toBeNull();
    fireEvent.mouseUp(document, { clientX: 140, clientY: 1050 });

    await waitFor(() => {
      expect(document.querySelector('[data-drag]')).toBeNull();
    });
    expect(fake.calls.transfer).toHaveLength(0);
    expect(fake.calls.reorderBaskets).toHaveLength(0);
  });

  it('reorders the owner’s baskets without touching the portfolio group, sending only their ids', async () => {
    stubLayout();
    const { fake } = renderBaskets({ portfolio: portfolioSeeds });
    await membersGrid();

    await dragWithKeyboard('Reorder MyChoice', ['{ArrowRight}']);

    await waitFor(() => {
      expect(fake.calls.reorderBaskets).toHaveLength(1);
    });
    expect(fake.calls.reorderBaskets[0]).toEqual([basketIds.myLongTerm, basketIds.myChoice, basketIds.bestStocks]);
    const cards = within(screen.getByRole('list', { name: 'Baskets' })).getAllByRole('listitem');
    expect(cards.map((card) => card.textContent.slice(0, 8))).toEqual([
      'Dhan · M',
      'Retireme',
      'MyLongTe',
      'MyChoice',
      'Best sto',
    ]);
  });
});

describe('Baskets page: a stray refusal of a portfolio change', () => {
  it('tells the owner portfolio baskets cannot be changed, whatever the server titled it', async () => {
    const { fake } = renderBaskets({ portfolio: portfolioSeeds });
    await membersGrid();
    fake.failNext('transfer', 409, 'baskets.system', 'Conflict');

    await userEvent.click(screen.getByRole('button', { name: 'Copy or move NSE:TCS' }));
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Copy to Best stocks' }));

    expect(await toast()).toHaveTextContent("Portfolio baskets can't be changed");
    expect(fake.symbolsIn(basketIds.bestStocks)).toEqual([]);
  });

  it('is what the fake answers to every change route of a portfolio basket', async () => {
    await openPortfolio();
    // The same rules as the API, so no screen can slip a change through unnoticed.
    const call = (method: string, path: string, body?: object) =>
      fetch(path, {
        method,
        headers: { 'content-type': 'application/json' },
        ...(body ? { body: JSON.stringify(body) } : {}),
      });
    const refused = await Promise.all([
      call('PUT', `/api/baskets/${dhan}`, { name: 'X', colour: '#112233', purpose: 'trading' }),
      call('DELETE', `/api/baskets/${dhan}`),
      call('POST', `/api/baskets/${dhan}/members`, { instrumentIds: ['x'] }),
      call('DELETE', `/api/baskets/${dhan}/members`, { instrumentIds: ['x'] }),
      call('PUT', `/api/baskets/${dhan}/members/order`, { instrumentIds: [] }),
      call('POST', '/api/baskets/transfer', {
        fromBasketId: basketIds.myChoice,
        toBasketId: dhan,
        instrumentIds: [],
        mode: 'copy',
      }),
      call('POST', '/api/baskets/transfer', {
        fromBasketId: dhan,
        toBasketId: basketIds.myChoice,
        instrumentIds: [],
        mode: 'move',
      }),
    ]);
    expect(refused.map((r) => r.status)).toEqual([409, 409, 409, 409, 409, 409, 409]);
    const bodies = await Promise.all(refused.map((r) => r.json() as Promise<{ code: string }>));
    expect(bodies.every((b) => b.code === 'baskets.system')).toBe(true);
    expect((await call('GET', `/api/baskets/${dhan}/history`)).status).toBe(400);
    expect((await call('GET', `/api/baskets/${dhan}/members?asOf=2026-10-01`)).status).toBe(400);
  });
});

describe('Baskets page: a portfolio basket on a phone', () => {
  it('shows one select with both groups', async () => {
    stubPhone();
    const { router } = renderBaskets({ portfolio: portfolioSeeds });

    const select = await screen.findByRole('combobox', { name: 'Basket' });
    const groups = within(select).getAllByRole('group');
    expect(groups.map((g) => g.getAttribute('label'))).toEqual(['Portfolio', 'Your baskets']);
    expect(
      within(groups[0]!)
        .getAllByRole('option')
        .map((o) => o.textContent),
    ).toEqual(['Dhan · Main · Portfolio (2)', 'Retirement bucket (0)']);
    expect(
      within(groups[1]!)
        .getAllByRole('option')
        .map((o) => o.textContent),
    ).toEqual(['MyChoice (3)', 'MyLongTerm (0)', 'Best stocks (0)']);

    await userEvent.selectOptions(select, 'Dhan · Main · Portfolio (2)');

    expect(await screen.findByRole('heading', { level: 2, name: 'Dhan · Main · Portfolio' })).toBeInTheDocument();
    expect(router.state.location.search).toBe(`?basket=${dhan}`);
    expect(screen.queryByRole('button', { name: 'Archive' })).not.toBeInTheDocument();
  });
});
