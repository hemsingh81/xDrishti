import { act, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';

/**
 * jsdom has no layout, so every element measures 0 x 0 and drag and drop cannot tell rows or cards apart. This gives
 * the member rows (stacked, below the cards) and the basket cards (a row across the top) rectangles, as a real screen would
 * have them. Call it in the test (restoreMocks undoes it).
 */
export function stubLayout() {
  const rect = (x: number, y: number, width: number, height: number): DOMRect => ({
    x,
    y,
    width,
    height,
    top: y,
    left: x,
    right: x + width,
    bottom: y + height,
    toJSON: () => ({}),
  });
  vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(function (this: Element) {
    const row = this.closest('tbody > tr');
    if (this instanceof HTMLTableRowElement && row === this && this.closest('tbody')) {
      const index = [...(this.parentElement?.children ?? [])].indexOf(this);
      return rect(300, 200 + index * 40, 600, 40);
    }
    // The cards are a row across the top: the owner's baskets (the ones with a grip) at y 100, 250 px apart. The
    // portfolio cards sit far below them: a drop there must find nothing (they are not drop targets).
    if (this instanceof HTMLLIElement && this.parentElement?.getAttribute('aria-label') === 'Baskets') {
      const cards = [...this.parentElement.children];
      const sortable = (card: Element) => card.querySelector('[aria-label^="Reorder "]') !== null;
      const mine = sortable(this);
      const index = cards.filter((card) => sortable(card) === mine).indexOf(this);
      return rect(index * 250, mine ? 100 : 1000, 240, 80);
    }
    // The drag overlay is a fixed box placed on the rect of the row it was lifted from (dnd-kit measures its first child).
    const placed = this.closest<HTMLElement>('[style*="position: fixed"]');
    if (placed && placed.style.width !== '1px') {
      const [left, top, width, height] = [
        placed.style.left,
        placed.style.top,
        placed.style.width,
        placed.style.height,
      ].map((value) => Number.parseFloat(value) || 0);
      return rect(left ?? 0, top ?? 0, width ?? 0, height ?? 0);
    }
    return rect(0, 0, 0, 0);
  });
}

/** Lets dnd-kit's keyboard sensor attach its key listener (it does so on the next tick). */
export const nextTick = () =>
  act(async () => {
    await new Promise<void>((resolve) => setTimeout(resolve, 0));
  });

const gripOf = (name: string) => screen.getByRole('button', { name });

/** The text a screen reader would be told last (dnd-kit's live region). */
export const announced = () => document.querySelector('[role="status"][aria-live="assertive"]')?.textContent ?? '';

/**
 * Picks up the row / card whose grip is called `grip`, presses the keys (e.g. '{ArrowDown}') and drops with space.
 * `held` keys (e.g. 'Alt') stay down during the drop.
 */
export async function dragWithKeyboard(grip: string, keys: readonly string[], held: string[] = []) {
  gripOf(grip).focus();
  await userEvent.keyboard(' ');
  await nextTick();
  for (const key of keys) {
    await userEvent.keyboard(key);
    await nextTick();
  }
  const down = held.map((key) => `{${key}>}`).join('');
  const up = held.map((key) => `{/${key}}`).join('');
  await userEvent.keyboard(`${down} ${up}`);
  await nextTick();
}
