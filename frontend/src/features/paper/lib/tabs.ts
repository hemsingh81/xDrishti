import { lazy, type ComponentType } from 'react';

/** The Paper screen's tabs, in order; a later slice adds one component and one line here. The first is the default. */
const entries: readonly { id: string; label: string; Component: ComponentType }[] = [
  {
    id: 'ledger',
    label: 'Ledger',
    Component: lazy(() => import('../components/LedgerTab').then((m) => ({ default: m.LedgerTab }))),
  },
  {
    id: 'review',
    label: 'Review',
    Component: lazy(() => import('../components/ReviewTab').then((m) => ({ default: m.ReviewTab }))),
  },
];

export type TabId = string;
export const tabs = entries;
export const DEFAULT_TAB: TabId = 'ledger';
