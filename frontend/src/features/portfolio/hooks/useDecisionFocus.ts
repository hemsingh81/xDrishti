import { useEffect, useRef, type RefObject } from 'react';
import type { RowActionKey } from '../lib/actions';

type Plan =
  { kind: 'after'; removed: string; next: string | null } | { kind: 'restore'; id: string; action: RowActionKey };

const focusOptions = { preventScroll: true } as const;

/**
 * Keeps the keyboard where it belongs while items come and go in the Action center. When an item is decided it leaves the
 * list: focus moves to the first button of the next row (the previous one at the end of the list), or to the card title
 * when the list is empty. When the server refuses and the item comes back, focus returns to the button that was used.
 * Nothing scrolls the page. `data` is whatever changes when the list does (the query's data).
 */
export function useDecisionFocus(title: RefObject<HTMLElement | null>, data: unknown) {
  const listRef = useRef<HTMLDivElement>(null);
  const plan = useRef<Plan | null>(null);
  const rows = () => [...(listRef.current?.querySelectorAll<HTMLElement>('li[data-item-id]') ?? [])];
  const rowOf = (id: string) => rows().find((row) => row.dataset.itemId === id);

  useEffect(() => {
    const current = plan.current;
    if (!current) return;
    if (current.kind === 'after') {
      if (rowOf(current.removed)) return;
      plan.current = null;
      const next = current.next === null ? undefined : rowOf(current.next);
      const button = next?.querySelector<HTMLElement>('[data-row-action]');
      (button ?? title.current)?.focus(focusOptions);
      return;
    }
    const row = rowOf(current.id);
    if (!row) return;
    plan.current = null;
    const button =
      row.querySelector<HTMLElement>(`[data-row-action="${current.action}"]`) ??
      row.querySelector<HTMLElement>('[data-row-action]');
    button?.focus(focusOptions);
    // The effect reads the DOM after each change of the list, nothing else.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [data]);

  return {
    listRef,
    /** The item is about to leave the list. */
    leaving: (id: string) => {
      const all = rows();
      const at = all.findIndex((row) => row.dataset.itemId === id);
      const neighbour = all[at + 1] ?? all[at - 1];
      plan.current = { kind: 'after', removed: id, next: neighbour?.dataset.itemId ?? null };
    },
    /** The server refused: the item is coming back, focus goes to the button that was used. */
    returning: (id: string, action: RowActionKey) => {
      plan.current = { kind: 'restore', id, action };
    },
  };
}
