import { createContext, useContext } from 'react';
import type { Basket } from '@/shared/api';

export interface MemberPanel {
  basket: Basket;
  /** The other active baskets: where members can be copied or moved. */
  others: readonly Basket[];
  /** A copy / move / remove call is running. */
  busy: boolean;
  transfer: (ids: readonly string[], toBasketId: string, mode: 'copy' | 'move', label: string) => Promise<boolean>;
  remove: (ids: readonly string[], label: string) => Promise<boolean>;
}

const unavailable = (): Promise<boolean> => Promise.resolve(false);

const none: Basket = {
  id: '',
  name: '',
  colour: '',
  purpose: 'trading',
  description: null,
  position: 0,
  memberCount: 0,
  createdAt: '',
  archivedAt: null,
};

export const MemberPanelContext = createContext<MemberPanel>({
  basket: none,
  others: [],
  busy: false,
  transfer: unavailable,
  remove: unavailable,
});

/** What the cells of the members grid act on: the shown basket and the copy / move / remove actions. */
export function useMemberPanel(): MemberPanel {
  return useContext(MemberPanelContext);
}
