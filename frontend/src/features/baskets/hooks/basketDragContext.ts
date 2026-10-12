import { createContext, useContext } from 'react';
import type { DragSession, DropTarget } from '../lib/dropRules';

export interface BasketDragView {
  session: DragSession | null;
  over: DropTarget | null;
  /** Alt / Option is held: a drop on a basket moves instead of copying. */
  move: boolean;
}

export const BasketDragContext = createContext<BasketDragView>({ session: null, over: null, move: false });

/** The drag that is running (if any), for the cards and rows that show where a drop would land. */
export function useBasketDrag(): BasketDragView {
  return useContext(BasketDragContext);
}
