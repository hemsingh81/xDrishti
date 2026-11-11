import type { Basket } from '@/shared/api';
import { isPortfolio, isStored, type KindFilter } from './system';

/** The owner's baskets (draggable, editable, valid targets), the portfolio ones and the stored system ones (protected, read only). */
export interface BasketGroups {
  /** Portfolio baskets, in the list's order: by account, then name. */
  portfolio: readonly Basket[];
  /** Stored system baskets (index, derived, list; BK-01), in the list's order. */
  system: readonly Basket[];
  /** The owner's baskets in their order. */
  manual: readonly Basket[];
}

export const splitBaskets = (baskets: readonly Basket[]): BasketGroups => ({
  portfolio: baskets.filter(isPortfolio),
  system: baskets.filter(isStored),
  manual: baskets.filter((b) => !b.system),
});

/** The groups the kind filter keeps (`all` keeps everything; `yours` only the owner's; a kind only that system kind). */
export function filterGroups(groups: BasketGroups, kind: KindFilter): BasketGroups {
  if (kind === 'all') return groups;
  if (kind === 'yours') return { portfolio: [], system: [], manual: groups.manual };
  return { portfolio: [], system: groups.system.filter((b) => b.kind === kind), manual: [] };
}

/** Only manual baskets: the ones instruments can be added or copied to. */
export const manualBaskets = (baskets: readonly Basket[]): Basket[] => baskets.filter((b) => !b.system);
