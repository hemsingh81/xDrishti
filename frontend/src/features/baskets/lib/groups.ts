import type { Basket } from '@/shared/api';

/** The baskets of the owner (draggable, editable, valid targets) and the portfolio ones (protected, read only). */
export interface BasketGroups {
  /** Portfolio (system) baskets, in the list's order: by account, then name. */
  portfolio: readonly Basket[];
  /** The owner's baskets in their order. */
  manual: readonly Basket[];
}

export const splitBaskets = (baskets: readonly Basket[]): BasketGroups => ({
  portfolio: baskets.filter((b) => b.system),
  manual: baskets.filter((b) => !b.system),
});

/** Only manual baskets: the ones instruments can be added or copied to. */
export const manualBaskets = (baskets: readonly Basket[]): Basket[] => baskets.filter((b) => !b.system);
