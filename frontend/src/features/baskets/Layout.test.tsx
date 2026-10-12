import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { buildBasket, seededBaskets } from '@/test/fixtures';
import { dragWithKeyboard, stubLayout } from './dndTestkit';
import { editableColumns, portfolioColumns } from './components/memberColumns';
import { basketCard, basketIds, membersGrid, portfolioSeeds, renderBaskets } from './testing';

const rowBox = (symbol: string) => screen.getByRole('checkbox', { name: `Select ${symbol}` });
const cardNames = () =>
  within(screen.getByRole('list', { name: 'Baskets' }))
    .getAllByRole('listitem')
    .map((card) => /^(.*?)(manual|portfolio)/.exec(card.textContent)?.[1]);

describe('Baskets page: the layout of the prototype', () => {
  it('has the title, the prototype subtitle and New basket on the title row', async () => {
    renderBaskets();

    expect(await screen.findByRole('heading', { level: 1, name: 'Baskets' })).toBeInTheDocument();
    expect(
      screen.getByText(
        'Group tracked instruments. Baskets define what the nightly plan scans, Strategy Lab universes, report lenses and portfolio grouping.',
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'New basket' })).toBeInTheDocument();
  });

  it('shows the cards as a grid across the top, each with kind and purpose chips and its count', async () => {
    renderBaskets();

    const list = await screen.findByRole('list', { name: 'Baskets' });
    expect(list).toHaveStyle({ display: 'grid' });
    const [first, second, third] = within(list).getAllByRole('listitem');
    expect(first).toHaveTextContent('MyChoicemanualtrading3 instruments');
    expect(second).toHaveTextContent('MyLongTermmanuallong-term0 instruments');
    expect(third).toHaveTextContent('Best stocksmanualtrading0 instruments');
    // The accent bar is the owner's colour, set on the card itself.
    expect(first).toHaveStyle({ borderTopColor: '#6366f1' });
    expect(second).toHaveStyle({ borderTopColor: '#10b981' });
  });

  it('marks the selected card and keeps portfolio cards first, ahead of the owner’s', async () => {
    renderBaskets({ portfolio: portfolioSeeds, search: `?basket=${basketIds.myLongTerm}` });

    await screen.findByRole('heading', { level: 2, name: 'MyLongTerm' });
    expect(cardNames()).toEqual([
      'Dhan · Main · Portfolio',
      'Retirement bucket',
      'MyChoice',
      'MyLongTerm',
      'Best stocks',
    ]);
    expect(basketCard('MyLongTerm')).toHaveAttribute('aria-current', 'true');
    expect(basketCard('MyChoice')).not.toHaveAttribute('aria-current');
    expect(basketCard('Dhan · Main · Portfolio')).not.toHaveAttribute('aria-current');
    // Only the owner’s cards carry a grip, always visible and reachable by keyboard.
    expect(screen.getAllByRole('button', { name: /^Reorder / })).toHaveLength(3);
  });

  it('puts the actions on the title row of the members card, with a line saying what kind of basket it is', async () => {
    renderBaskets();
    await membersGrid();

    expect(screen.getByRole('heading', { level: 2, name: 'MyChoice' })).toBeInTheDocument();
    expect(screen.getByText('Manual — you add and remove instruments.')).toBeInTheDocument();
    for (const name of ['History', 'Edit', 'Archive']) {
      expect(screen.getByRole('button', { name })).toBeInTheDocument();
    }
    expect(screen.getByLabelText('View as of')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Add instruments' })).not.toBeInTheDocument();
  });

  it('shows the basket’s own description in place of the standard line', async () => {
    renderBaskets({
      baskets: [buildBasket({ description: 'Large caps I would buy on a dip' }), ...seededBaskets().slice(1)],
    });

    expect(await screen.findByText('Large caps I would buy on a dip')).toBeInTheDocument();
    expect(screen.queryByText('Manual — you add and remove instruments.')).not.toBeInTheDocument();
  });

  it('says what a portfolio basket is and offers no actions, only its chip', async () => {
    renderBaskets({ portfolio: portfolioSeeds, search: `?basket=${basketIds.dhanPortfolio}` });

    expect(await screen.findByRole('heading', { level: 2, name: 'Dhan · Main · Portfolio' })).toBeInTheDocument();
    expect(screen.getByText('Portfolio · read only')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'History' })).not.toBeInTheDocument();
  });

  it('puts the two insight cards beside the members with honest empty states instead of numbers', async () => {
    renderBaskets();
    await membersGrid();

    const performance = screen.getByRole('region', { name: 'Basket performance (taken trades)' });
    expect(
      within(performance).getByText("Appears once trades have been taken in this basket's instruments."),
    ).toBeInTheDocument();
    const benchmark = screen.getByRole('region', { name: '1-year buy-and-hold benchmark' });
    expect(
      within(benchmark).getByText('Needs price history — available after the EOD service (P1-12).'),
    ).toBeInTheDocument();
  });
});

describe('Baskets page: the add row', () => {
  it('opens the Add instruments dialog on Enter with the typed text as its search', async () => {
    renderBaskets();
    await membersGrid();

    await userEvent.type(screen.getByRole('textbox', { name: 'Add instrument' }), 'yes{Enter}');

    const dialog = await screen.findByRole('dialog', { name: 'Add instruments to MyChoice' });
    const search = within(dialog).getByRole('searchbox', { name: 'Search instruments' });
    expect(search).toHaveValue('yes');
    expect(await within(dialog).findByRole('checkbox', { name: /NSE:YESBANK/ })).toBeEnabled();
    expect(within(dialog).queryByRole('checkbox', { name: /NSE:TINYCO/ })).not.toBeInTheDocument();
  });

  it('opens it with the Add button, and with an empty search when nothing was typed', async () => {
    renderBaskets();
    await membersGrid();
    expect(screen.getByPlaceholderText('Add instrument (symbol)…')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Add' }));

    const dialog = await screen.findByRole('dialog', { name: 'Add instruments to MyChoice' });
    expect(within(dialog).getByRole('searchbox', { name: 'Search instruments' })).toHaveValue('');
  });

  it('is not offered in a past view', async () => {
    renderBaskets({ search: '?asOf=2026-10-01' });

    await screen.findByText(/Showing the members as they were/);
    expect(screen.queryByRole('textbox', { name: 'Add instrument' })).not.toBeInTheDocument();
  });
});

describe('Baskets page: the selection toolbar', () => {
  it('is there only while something is selected', async () => {
    renderBaskets();
    await membersGrid();
    expect(screen.queryByRole('button', { name: 'Copy to…' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Remove \(/ })).not.toBeInTheDocument();

    await userEvent.click(rowBox('NSE:TCS'));
    await userEvent.click(rowBox('NSE:RELIANCE'));

    expect(screen.getByText('2 selected')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Copy to…' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Move to…' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Remove (2)' })).toBeEnabled();

    await userEvent.click(screen.getByRole('button', { name: 'Clear selection' }));

    expect(screen.queryByRole('button', { name: 'Copy to…' })).not.toBeInTheDocument();
  });
});

describe('Baskets page: columns are chosen by width, not scrolled', () => {
  const hiddenBelow = (columns: typeof editableColumns) =>
    Object.fromEntries(columns.map((column) => [column.id, column.hideBelow ?? 'always']));

  it('keeps grip, symbol, status and actions always, and adds type, then name and sector as the page widens (no added date)', () => {
    expect(hiddenBelow(editableColumns)).toEqual({
      order: 'always',
      symbol: 'always',
      name: 'xl',
      type: 'lg',
      sector: 'xl',
      status: 'always',
      actions: 'always',
    });
  });

  it('keeps a portfolio basket’s quantity always and moves type and account to the widest screens', () => {
    expect(hiddenBelow(portfolioColumns)).toEqual({
      copy: 'always',
      symbol: 'always',
      name: 'xl',
      type: 'xl',
      sector: 'xl',
      status: 'always',
      quantity: 'always',
      account: 'xl',
      actions: 'always',
    });
  });

  it('fits the always-visible columns of the editable view in the members card at 1280 px', () => {
    // Content width at 1280 px is 984 px; the card gets 3/5 of it less the gap: ~581 px. The selection column is 48 px;
    // symbol (~115), status (~110) and type (~70) size to their content, the grip and actions have fixed widths.
    const fixedWidths = editableColumns
      .filter((column) => column.hideBelow === undefined || column.hideBelow === 'lg')
      .reduce((total, column) => total + (typeof column.width === 'number' ? column.width : 0), 0);
    expect(48 + fixedWidths + 115 + 110 + 70).toBeLessThan(581);
  });
});

describe('Baskets page: reordering the cards in a row', () => {
  it('moves a card left, then further left, with the keyboard (rect sorting strategy of a wrapping grid)', async () => {
    stubLayout();
    const { fake } = renderBaskets();
    await membersGrid();

    await dragWithKeyboard('Reorder Best stocks', ['{ArrowLeft}']);

    expect(cardNames()).toEqual(['MyChoice', 'Best stocks', 'MyLongTerm']);
    expect(fake.calls.reorderBaskets).toEqual([[basketIds.myChoice, basketIds.bestStocks, basketIds.myLongTerm]]);

    await dragWithKeyboard('Reorder Best stocks', ['{ArrowLeft}']);

    expect(cardNames()).toEqual(['Best stocks', 'MyChoice', 'MyLongTerm']);
  });
});
