import type { InfiniteData } from '@tanstack/react-query';
import type { PriceAlertPage } from '@/shared/api';
import type { AlertStatus } from './alerts';

/**
 * The page as it will be once an active alert is cancelled: gone from the Active list, one fewer active (the page carries
 * the active count whatever its filter). The Cancelled list and its count are left to the reload that follows.
 */
export function withoutActiveAlert(page: PriceAlertPage, id: string, listed: AlertStatus): PriceAlertPage {
  const had = page.items.some((alert) => alert.id === id);
  const activeCount = Math.max(0, page.activeCount - 1);
  if (listed !== 'active') return { ...page, activeCount };
  return {
    ...page,
    items: had ? page.items.filter((alert) => alert.id !== id) : page.items,
    // A count read (page size 1) does not list the alert but counts it.
    total: Math.max(0, page.total - 1),
    activeCount,
  };
}

/** The same over the pages of a loaded list: only the page that held the alert changes its items; every page counts. */
export function withoutActiveAlertPages(
  data: InfiniteData<PriceAlertPage>,
  id: string,
  listed: AlertStatus,
): InfiniteData<PriceAlertPage> {
  return { ...data, pages: data.pages.map((page) => withoutActiveAlert(page, id, listed)) };
}
