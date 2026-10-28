import { lazy, type ComponentType } from 'react';

/** The Live screen's tabs, in order; a later slice adds one component and one line here. The first is the default. */
const entries: readonly { id: string; label: string; Component: ComponentType }[] = [
  {
    id: 'compliance',
    label: 'Compliance',
    Component: lazy(() => import('../components/ComplianceTab').then((m) => ({ default: m.ComplianceTab }))),
  },
  {
    id: 'ramp',
    label: 'Ramp',
    Component: lazy(() => import('../components/RampTab').then((m) => ({ default: m.RampTab }))),
  },
];

export type TabId = string;
export const tabs = entries;
export const DEFAULT_TAB: TabId = 'compliance';
