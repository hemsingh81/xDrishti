import type { InfiniteData } from '@tanstack/react-query';
import type { Trade, TradePage } from '@/shared/api';

/** The pages as they will be once a trade is voided: the same list with that trade struck out (the server fills in who). */
export function withVoidedTrade(data: InfiniteData<TradePage>, id: string, voidedAt: string): InfiniteData<TradePage> {
  return {
    ...data,
    pages: data.pages.map((page) => ({
      ...page,
      items: page.items.map((trade): Trade => (trade.id === id ? { ...trade, voided: true, voidedAt } : trade)),
    })),
  };
}

/** The trade `id` as the cached lists hold it, if any of them does. */
export function findTrade(lists: readonly (InfiniteData<TradePage> | undefined)[], id: string): Trade | undefined {
  for (const data of lists) {
    for (const page of data?.pages ?? []) {
      const found = page.items.find((trade) => trade.id === id);
      if (found) return found;
    }
  }
  return undefined;
}

/** The pages with this one trade put back as it was (every other trade is left as it is now: a rollback is per trade). */
export function withTrade(data: InfiniteData<TradePage>, trade: Trade): InfiniteData<TradePage> {
  return {
    ...data,
    pages: data.pages.map((page) => ({
      ...page,
      items: page.items.map((item) => (item.id === trade.id ? trade : item)),
    })),
  };
}
