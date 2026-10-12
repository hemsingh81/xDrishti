import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { announced, dragWithKeyboard, nextTick, stubLayout } from './dndTestkit';
import { basketIds, membersGrid, renderBaskets, symbolsShown } from './testing';

const rowBox = (symbol: string) => screen.getByRole('checkbox', { name: `Select ${symbol}` });
const cardNames = () =>
  within(screen.getByRole('list', { name: 'Baskets' }))
    .getAllByRole('listitem')
    .map((card) => /^(.*?)(Trading|Long-term|Watch)/.exec(card.textContent)?.[1]);

describe('Baskets page: reordering members', () => {
  it('moves a member down with the keyboard and tells the screen reader', async () => {
    stubLayout();
    const { fake } = renderBaskets();
    await membersGrid();

    await dragWithKeyboard('Drag NSE:TCS', ['{ArrowDown}']);

    expect(symbolsShown()).toEqual(['NSE:RELIANCE', 'NSE:TCS', 'NSE:HDFCBANK']);
    expect(announced()).toBe('Dropped NSE:TCS on NSE:RELIANCE, position 2 of 3.');
    await screen.findByRole('row', { name: /NSE:TCS/ });
    expect(fake.calls.reorderMembers).toHaveLength(1);
    expect(fake.symbolsIn(basketIds.myChoice)).toEqual(['NSE:RELIANCE', 'NSE:TCS', 'NSE:HDFCBANK']);
  });

  it('describes the drag while it runs', async () => {
    stubLayout();
    renderBaskets();
    await membersGrid();

    const grip = screen.getByRole('button', { name: 'Drag NSE:RELIANCE' });
    grip.focus();
    await userEvent.keyboard(' ');
    await nextTick();
    expect(announced()).toBe('NSE:RELIANCE is over NSE:RELIANCE, position 2 of 3.');
    await userEvent.keyboard('{ArrowUp}');
    await nextTick();
    expect(announced()).toBe('NSE:RELIANCE is over NSE:TCS, position 1 of 3.');
    await userEvent.keyboard('{Escape}');
    await nextTick();
    expect(announced()).toBe('Cancelled. NSE:RELIANCE stays where it was.');
    expect(symbolsShown()).toEqual(['NSE:TCS', 'NSE:RELIANCE', 'NSE:HDFCBANK']);
  });

  it('moves the whole selection as a block when a selected row is dragged', async () => {
    stubLayout();
    const { fake } = renderBaskets();
    await membersGrid();
    await userEvent.click(rowBox('NSE:RELIANCE'));
    await userEvent.click(rowBox('NSE:HDFCBANK'));

    await dragWithKeyboard('Drag NSE:HDFCBANK', ['{ArrowUp}', '{ArrowUp}']);

    expect(symbolsShown()).toEqual(['NSE:RELIANCE', 'NSE:HDFCBANK', 'NSE:TCS']);
    expect(fake.calls.reorderMembers).toHaveLength(1);
  });

  it('puts the order back and says so when the server refuses the new order', async () => {
    stubLayout();
    const { fake } = renderBaskets();
    await membersGrid();
    fake.failNext('reorder-members', 409, 'baskets.conflict', 'Changed meanwhile.');

    await dragWithKeyboard('Drag NSE:TCS', ['{ArrowDown}']);

    expect(await screen.findByText(/changed somewhere else at the same moment/)).toBeInTheDocument();
    await screen.findByRole('row', { name: /NSE:TCS/ });
    expect(symbolsShown()).toEqual(['NSE:TCS', 'NSE:RELIANCE', 'NSE:HDFCBANK']);
  });

  it('reloads the members and says so when the order is refused because the basket changed', async () => {
    stubLayout();
    const { fake } = renderBaskets();
    await membersGrid();
    const loads = fake.calls.members.length;
    fake.failNext(
      'reorder-members',
      400,
      'baskets.invalid',
      'The list must hold every instrument of the basket exactly once.',
    );

    await dragWithKeyboard('Drag NSE:TCS', ['{ArrowDown}']);

    expect(await screen.findByText('The basket changed; it has been reloaded.')).toBeInTheDocument();
    await waitFor(() => {
      expect(fake.calls.members.length).toBeGreaterThan(loads);
    });
    expect(symbolsShown()).toEqual(['NSE:TCS', 'NSE:RELIANCE', 'NSE:HDFCBANK']);
  });

  it('does nothing when the drag is dropped where it started', async () => {
    stubLayout();
    const { fake } = renderBaskets();
    await membersGrid();

    await dragWithKeyboard('Drag NSE:TCS', []);

    expect(fake.calls.reorderMembers).toHaveLength(0);
    expect(symbolsShown()).toEqual(['NSE:TCS', 'NSE:RELIANCE', 'NSE:HDFCBANK']);
  });
});

describe('Baskets page: reordering baskets', () => {
  it('reorders the cards with the keyboard', async () => {
    stubLayout();
    const { fake } = renderBaskets();
    await membersGrid();
    expect(cardNames()).toEqual(['MyChoice', 'MyLongTerm', 'Best stocks']);

    await dragWithKeyboard('Reorder MyChoice', ['{ArrowDown}']);

    expect(cardNames()).toEqual(['MyLongTerm', 'MyChoice', 'Best stocks']);
    expect(fake.calls.reorderBaskets).toEqual([[basketIds.myLongTerm, basketIds.myChoice, basketIds.bestStocks]]);
    expect(fake.names()).toEqual(['MyLongTerm', 'MyChoice', 'Best stocks']);
  });

  it('restores the order when the server refuses it', async () => {
    stubLayout();
    const { fake } = renderBaskets();
    await membersGrid();
    fake.failNext('reorder-baskets', 409, 'baskets.conflict', 'Changed meanwhile.');

    await dragWithKeyboard('Reorder MyChoice', ['{ArrowDown}']);

    expect(await screen.findByText(/changed somewhere else at the same moment/)).toBeInTheDocument();
    await screen.findAllByRole('listitem');
    expect(cardNames()).toEqual(['MyChoice', 'MyLongTerm', 'Best stocks']);
  });

  it('reloads the baskets and says so when the card order is refused because the list changed', async () => {
    stubLayout();
    const { fake } = renderBaskets();
    await membersGrid();
    fake.failNext('reorder-baskets', 400, 'baskets.invalid', 'The list must hold every basket exactly once.');

    await dragWithKeyboard('Reorder MyChoice', ['{ArrowDown}']);

    expect(await screen.findByText('The basket changed; it has been reloaded.')).toBeInTheDocument();
    await screen.findAllByRole('listitem');
    expect(cardNames()).toEqual(['MyChoice', 'MyLongTerm', 'Best stocks']);
  });
});
