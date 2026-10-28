import { lazy, type ComponentType } from 'react';

/** The Live screen's tabs, in order; a later slice adds one component and one line here. The first is the default. */
const entries: readonly { id: string; label: string; Component: ComponentType }[] = [
  {
    id: 'compliance',
    label: 'Compliance',
    Component: lazy(() => import('../components/ComplianceTab').then((m) => ({ default: m.ComplianceTab }))),
  },
  {
    id: 'capture',
    label: 'Capture',
    Component: lazy(() => import('../components/CaptureTab').then((m) => ({ default: m.CaptureTab }))),
  },
  {
    id: 'execution',
    label: 'Execution',
    Component: lazy(() => import('../components/ExecutionTab').then((m) => ({ default: m.ExecutionTab }))),
  },
  {
    id: 'vs-paper',
    label: 'Live vs paper',
    Component: lazy(() => import('../components/LiveVsPaperTab').then((m) => ({ default: m.LiveVsPaperTab }))),
  },
  {
    id: 'ramp',
    label: 'Ramp',
    Component: lazy(() => import('../components/RampTab').then((m) => ({ default: m.RampTab }))),
  },
  {
    id: 'phase-check',
    label: 'Phase check',
    Component: lazy(() => import('../components/PhaseCheckTab').then((m) => ({ default: m.PhaseCheckTab }))),
  },
];

export type TabId = string;
export const tabs = entries;
export const DEFAULT_TAB: TabId = 'compliance';
