/** The two tabs of the Trades panel; the URL carries the open one (`?trades=import`). */
export const tradesTabs = ['import', 'history'] as const;
export type TradesTab = (typeof tradesTabs)[number];

export const tradesTabLabels: Record<TradesTab, string> = { import: 'Import CSV', history: 'History' };

export const isTradesTab = (value: string | null): value is TradesTab => tradesTabs.some((tab) => tab === value);

export const tradesTabId = (tab: TradesTab): string => `trades-tab-${tab}`;
export const tradesPanelId = (tab: TradesTab): string => `trades-panel-${tab}`;
