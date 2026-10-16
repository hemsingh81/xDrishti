import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { dragWithKeyboard, announced, stubLayout } from './dndTestkit';
import { basketCard, basketIds, membersGrid, renderBaskets, symbolsShown } from './testing';
import { findToast } from '@/test/toast';

const rowBox = (symbol: string) => screen.getByRole('checkbox', { name: `Select ${symbol}` });
const toast = findToast;

async function pickFromRowMenu(symbol: string, item: string) {
  await userEvent.click(screen.getByRole('button', { name: `Copy or move ${symbol}` }));
  await userEvent.click(await screen.findByRole('menuitem', { name: item }));
}

describe('Baskets page: copy and move with the menus', () => {
  it('copies a row to another basket, and Undo takes it out again', async () => {
    const { fake } = renderBaskets();
    await membersGrid();

    await pickFromRowMenu('NSE:TCS', 'Copy to Best stocks');

    expect(await toast()).toHaveTextContent('Copied NSE:TCS to Best stocks (1 was not tracked and now is)');
    expect(fake.symbolsIn(basketIds.bestStocks)).toEqual(['NSE:TCS']);
    expect(fake.symbolsIn(basketIds.myChoice)).toEqual(['NSE:TCS', 'NSE:RELIANCE', 'NSE:HDFCBANK']);
    expect(basketCard('Best stocks')).toHaveTextContent('1 instrument');

    await userEvent.click(screen.getByRole('button', { name: 'Undo' }));

    expect(await screen.findByText(/Copy undone: removed NSE:TCS from Best stocks/)).toBeInTheDocument();
    expect(fake.symbolsIn(basketIds.bestStocks)).toEqual([]);
    expect(fake.symbolsIn(basketIds.myChoice)).toHaveLength(3);
    // The instrument was tracked on the way; undoing the copy does not untrack it, and says so.
    expect(screen.getByText(/stay tracked/)).toBeInTheDocument();
  });

  it('copies and undoes with the keyboard alone: focus goes to Undo and comes back', async () => {
    const { fake } = renderBaskets();
    await membersGrid();
    const trigger = screen.getByRole('button', { name: 'Copy or move NSE:TCS' });
    trigger.focus();

    await userEvent.keyboard('{Enter}');
    await screen.findByRole('menu', { name: 'Copy or move NSE:TCS' });
    await userEvent.keyboard('{ArrowDown}{Enter}');

    expect(await toast()).toHaveTextContent('Copied NSE:TCS to Best stocks');
    const undo = screen.getByRole('button', { name: 'Undo' });
    await waitFor(() => {
      expect(undo).toHaveFocus();
    });
    expect(fake.symbolsIn(basketIds.bestStocks)).toEqual(['NSE:TCS']);

    await userEvent.keyboard('{Enter}');

    expect(await screen.findByText(/Copy undone: removed NSE:TCS from Best stocks/)).toBeInTheDocument();
    expect(fake.symbolsIn(basketIds.bestStocks)).toEqual([]);
    expect(trigger).toHaveFocus();
  });

  it('undoes a copy with Ctrl+Z', async () => {
    const { fake } = renderBaskets();
    await membersGrid();
    await pickFromRowMenu('NSE:TCS', 'Copy to Best stocks');
    await toast();

    await userEvent.keyboard('{Control>}z{/Control}');

    expect(await screen.findByText(/Copy undone/)).toBeInTheDocument();
    expect(fake.symbolsIn(basketIds.bestStocks)).toEqual([]);
  });

  it('moves a row, and Undo brings it back to its place with its order', async () => {
    const { fake } = renderBaskets();
    await membersGrid();

    await pickFromRowMenu('NSE:RELIANCE', 'Move to MyLongTerm');

    expect(await toast()).toHaveTextContent('Moved NSE:RELIANCE to MyLongTerm');
    await waitFor(() => {
      expect(symbolsShown()).toEqual(['NSE:TCS', 'NSE:HDFCBANK']);
    });
    expect(fake.symbolsIn(basketIds.myLongTerm)).toEqual(['NSE:RELIANCE']);

    await userEvent.click(screen.getByRole('button', { name: 'Undo' }));

    await waitFor(() => {
      expect(symbolsShown()).toEqual(['NSE:TCS', 'NSE:RELIANCE', 'NSE:HDFCBANK']);
    });
    expect(fake.symbolsIn(basketIds.myLongTerm)).toEqual([]);
    expect(await screen.findByText(/Move undone/)).toBeInTheDocument();
  });

  it('copies and moves the selection from the toolbar', async () => {
    const { fake } = renderBaskets();
    await membersGrid();
    await userEvent.click(rowBox('NSE:TCS'));
    await userEvent.click(rowBox('NSE:HDFCBANK'));

    await userEvent.click(screen.getByRole('button', { name: 'Copy to…' }));
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Copy to Best stocks' }));

    expect(await toast()).toHaveTextContent('Copied 2 instruments to Best stocks');
    expect(fake.symbolsIn(basketIds.bestStocks)).toEqual(['NSE:TCS', 'NSE:HDFCBANK']);
    expect(screen.queryByRole('button', { name: 'Clear selection' })).not.toBeInTheDocument();

    await userEvent.click(rowBox('NSE:RELIANCE'));
    await userEvent.click(screen.getByRole('button', { name: 'Move to…' }));
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Move to MyLongTerm' }));

    await waitFor(() => {
      expect(symbolsShown()).toEqual(['NSE:TCS', 'NSE:HDFCBANK']);
    });
    expect(fake.symbolsIn(basketIds.myLongTerm)).toEqual(['NSE:RELIANCE']);
  });

  it('does not offer the basket that is shown as a target', async () => {
    renderBaskets();
    await membersGrid();

    await userEvent.click(screen.getByRole('button', { name: 'Copy or move NSE:TCS' }));

    const menu = await screen.findByRole('menu', { name: 'Copy or move NSE:TCS' });
    expect(
      within(menu)
        .getAllByRole('menuitem')
        .map((item) => item.getAttribute('aria-label')),
    ).toEqual(['Copy to MyLongTerm', 'Copy to Best stocks', 'Move to MyLongTerm', 'Move to Best stocks']);
  });

  it('says nothing changed when everything is in the target already, without an Undo', async () => {
    const { fake } = renderBaskets({
      members: { [basketIds.myChoice]: ['NSE:TCS'], [basketIds.bestStocks]: ['NSE:TCS'] },
    });
    await membersGrid();

    await pickFromRowMenu('NSE:TCS', 'Copy to Best stocks');

    expect(await toast()).toHaveTextContent('Nothing changed: NSE:TCS already in Best stocks');
    expect(screen.queryByRole('button', { name: 'Undo' })).not.toBeInTheDocument();
    expect(fake.symbolsIn(basketIds.bestStocks)).toEqual(['NSE:TCS']);
  });

  it('reloads and asks to try again after a conflict', async () => {
    const { fake } = renderBaskets();
    await membersGrid();
    fake.failNext('transfer', 409, 'baskets.conflict', 'Changed meanwhile.');

    await pickFromRowMenu('NSE:TCS', 'Copy to Best stocks');

    expect(await toast()).toHaveTextContent('changed somewhere else at the same moment');
    expect(fake.symbolsIn(basketIds.bestStocks)).toEqual([]);
    expect(symbolsShown()).toEqual(['NSE:TCS', 'NSE:RELIANCE', 'NSE:HDFCBANK']);
  });
});

describe('Baskets page: drag and drop onto a basket', () => {
  it('copies a row dropped on another basket with the keyboard', async () => {
    stubLayout();
    const { fake } = renderBaskets();
    await membersGrid();

    await dragWithKeyboard('Drag NSE:TCS', ['{ArrowUp}']);

    expect(announced()).toBe('Dropped NSE:TCS on basket Best stocks.');
    expect(await toast()).toHaveTextContent(/^Copied NSE:TCS to Best stocks/);
    expect(screen.getByRole('button', { name: 'Undo' })).toBeInTheDocument();
    expect(fake.calls.transfer).toEqual([expect.objectContaining({ mode: 'copy', fromBasketId: basketIds.myChoice })]);
    expect(symbolsShown()).toEqual(['NSE:TCS', 'NSE:RELIANCE', 'NSE:HDFCBANK']);
  });

  it('moves the row when Alt is held at the drop', async () => {
    stubLayout();
    const { fake } = renderBaskets();
    await membersGrid();

    await dragWithKeyboard('Drag NSE:TCS', ['{ArrowUp}'], ['Alt']);

    expect(await toast()).toHaveTextContent(/^Moved NSE:TCS to Best stocks/);
    expect(fake.calls.transfer).toEqual([expect.objectContaining({ mode: 'move', toBasketId: basketIds.bestStocks })]);
    await waitFor(() => {
      expect(symbolsShown()).toEqual(['NSE:RELIANCE', 'NSE:HDFCBANK']);
    });
  });

  it('drags the whole selection', async () => {
    stubLayout();
    const { fake } = renderBaskets();
    await membersGrid();
    await userEvent.click(rowBox('NSE:TCS'));
    await userEvent.click(rowBox('NSE:RELIANCE'));

    await dragWithKeyboard('Drag NSE:TCS', ['{ArrowUp}']);

    expect(await toast()).toHaveTextContent('Copied 2 instruments to Best stocks');
    expect(fake.symbolsIn(basketIds.bestStocks)).toEqual(['NSE:TCS', 'NSE:RELIANCE']);
  });

  it('copies with the mouse and moves with Alt, highlighting the card under the pointer', async () => {
    stubLayout();
    const { fake } = renderBaskets();
    await membersGrid();
    const grip = screen.getByRole('button', { name: 'Drag NSE:TCS' });
    // Cards are a row across the top (250 px apart); the second one (MyLongTerm) is at x 250-490.
    fireEvent.mouseDown(grip, { button: 0, clientX: 320, clientY: 220 });
    fireEvent.mouseMove(document, { clientX: 400, clientY: 150 });
    fireEvent.mouseMove(document, { clientX: 370, clientY: 140 });
    const hot = document.querySelector('[data-drop-target]');
    expect(hot).toHaveAttribute('data-drop-target', 'copy');
    expect(hot).toHaveTextContent('MyLongTerm');

    fireEvent.mouseUp(document, { clientX: 370, clientY: 140 });

    expect(await toast()).toHaveTextContent(/^Copied NSE:TCS to MyLongTerm/);
    expect(fake.calls.transfer).toEqual([expect.objectContaining({ mode: 'copy' })]);
  });
});
