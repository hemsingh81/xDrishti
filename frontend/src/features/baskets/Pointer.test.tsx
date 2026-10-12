import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { stubLayout } from './dndTestkit';
import { basketIds, membersGrid, renderBaskets, symbolsShown } from './testing';

const toast = () => screen.findByRole('alert');
const grip = (symbol: string) => screen.getByRole('button', { name: `Drag ${symbol}` });
// Rows are stacked from y = 200 (40 high) at x 300-900; the cards from y = 100 (90 apart) at x 0-280.
const overRow = (index: number) => ({ clientX: 500, clientY: 200 + index * 40 + 20 });
const overCard = (index: number) => ({ clientX: 140, clientY: 100 + index * 90 + 40 });

describe('Baskets page: drag with the mouse and touch', () => {
  it('reorders by dragging a row onto another row', async () => {
    stubLayout();
    const { fake } = renderBaskets();
    await membersGrid();

    fireEvent.mouseDown(grip('NSE:TCS'), { button: 0, ...overRow(0) });
    fireEvent.mouseMove(document, overRow(1));
    fireEvent.mouseMove(document, overRow(2));
    expect(document.querySelector('[data-drop="below"]')).toHaveTextContent('NSE:HDFCBANK');
    fireEvent.mouseUp(document, overRow(2));

    await waitFor(() => {
      expect(symbolsShown()).toEqual(['NSE:RELIANCE', 'NSE:HDFCBANK', 'NSE:TCS']);
    });
    expect(fake.symbolsIn(basketIds.myChoice)).toEqual(['NSE:RELIANCE', 'NSE:HDFCBANK', 'NSE:TCS']);
  });

  it('moves instead of copying when Option is held while the row is dropped on a card', async () => {
    stubLayout();
    const { fake } = renderBaskets();
    await membersGrid();

    fireEvent.mouseDown(grip('NSE:TCS'), { button: 0, ...overRow(0) });
    fireEvent.mouseMove(document, overCard(1));
    fireEvent.mouseMove(document, { ...overCard(1), altKey: true });
    expect(document.querySelector('[data-drop-target]')).toHaveAttribute('data-drop-target', 'move');
    expect(document.querySelector('[data-drop-target]')).toHaveTextContent('Drop to move here');
    fireEvent.mouseUp(document, { ...overCard(1), altKey: true });

    expect(await toast()).toHaveTextContent(/^Moved NSE:TCS to MyLongTerm/);
    expect(fake.calls.transfer).toEqual([expect.objectContaining({ mode: 'move' })]);
  });

  it('leaves everything alone when a row is dropped on its own basket', async () => {
    stubLayout();
    const { fake } = renderBaskets();
    await membersGrid();

    fireEvent.mouseDown(grip('NSE:TCS'), { button: 0, ...overRow(0) });
    fireEvent.mouseMove(document, overCard(0));
    expect(document.querySelector('[data-drop-target]')).toBeNull();
    fireEvent.mouseUp(document, overCard(0));

    await act(async () => {
      await Promise.resolve();
    });
    expect(fake.calls.transfer).toHaveLength(0);
    expect(fake.calls.reorderMembers).toHaveLength(0);
  });

  it('copies with a long press and a drag on a touch screen', async () => {
    stubLayout();
    const { fake } = renderBaskets();
    await membersGrid();
    const handle = grip('NSE:TCS');

    fireEvent.touchStart(handle, { touches: [overRow(0)] });
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 250));
    });
    fireEvent.touchMove(handle, { touches: [overCard(2)] });
    fireEvent.touchEnd(handle, { touches: [], changedTouches: [overCard(2)] });

    expect(await toast()).toHaveTextContent(/^Copied NSE:TCS to Best stocks/);
    expect(fake.calls.transfer).toEqual([expect.objectContaining({ mode: 'copy', toBasketId: basketIds.bestStocks })]);
  });
});
