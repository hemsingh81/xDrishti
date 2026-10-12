import type { InstrumentTab } from './filters';

/** Ids that tie the tabs to the panel below them. */
export const tabId = (tab: InstrumentTab) => `instruments-tab-${tab}`;
export const panelId = 'instruments-panel';
