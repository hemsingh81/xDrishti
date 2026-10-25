import { lazy, type ComponentType } from 'react';

/** What the frame gives every tab: a way to switch to another tab by id. */
export interface TabProps {
  open: (id: TabId) => void;
}

/** A registry entry as written: the tab's code is fetched when it is first shown. */
interface TabEntry<Id extends string = string> {
  /** The `tab=` value in the address; the first entry is the default and is kept out of the address. */
  id: Id;
  label: string;
  /** The slice that built it. */
  slice: string;
  load: () => Promise<{ default: ComponentType<TabProps> }>;
}

/**
 * The tabs that exist, in order. Adding a tab is one new component file and one line here (P8-03, P8-04, P8-12 and P8-13 do
 * exactly that).
 */
const entries = [
  {
    id: 'overview',
    label: 'Overview',
    slice: 'P8-02',
    load: () => import('../components/OverviewTab').then((m) => ({ default: m.OverviewTab })),
  },
  {
    id: 'setups',
    label: 'Setups',
    slice: 'P8-02',
    load: () => import('../components/SetupsTab').then((m) => ({ default: m.SetupsTab })),
  },
  {
    id: 'lessons',
    label: 'Lessons',
    slice: 'P8-03',
    load: () => import('../components/LessonsTab').then((m) => ({ default: m.LessonsTab })),
  },
  {
    id: 'experiments',
    label: 'Experiments',
    slice: 'P8-03',
    load: () => import('../components/ExperimentsTab').then((m) => ({ default: m.ExperimentsTab })),
  },
  {
    id: 'models',
    label: 'Models',
    slice: 'P8-03',
    load: () => import('../components/ModelsTab').then((m) => ({ default: m.ModelsTab })),
  },
  {
    id: 'digest',
    label: 'Digest',
    slice: 'P8-03',
    load: () => import('../components/DigestTab').then((m) => ({ default: m.DigestTab })),
  },
  {
    id: 'plan',
    label: 'Plan vs actual',
    slice: 'P8-04',
    load: () => import('../components/PlanVsActualTab').then((m) => ({ default: m.PlanVsActualTab })),
  },
  {
    id: 'decisions',
    label: 'Decisions',
    slice: 'P8-04',
    load: () => import('../components/DecisionsTab').then((m) => ({ default: m.DecisionsTab })),
  },
  {
    id: 'autonomy',
    label: 'Autonomy',
    slice: 'P8-04',
    load: () => import('../components/AutonomyTab').then((m) => ({ default: m.AutonomyTab })),
  },
  {
    id: 'checks',
    label: 'Phase checks',
    slice: 'P8-04',
    load: () => import('../components/ChecksTab').then((m) => ({ default: m.ChecksTab })),
  },
  {
    id: 'journey',
    label: 'Journey',
    slice: 'P8-12',
    load: () => import('../journey/JourneyTab').then((m) => ({ default: m.JourneyTab })),
  },
  {
    id: 'smart-rules',
    label: 'Smart rules',
    slice: 'P8-13',
    load: () => import('../components/SmartRulesTab').then((m) => ({ default: m.SmartRulesTab })),
  },
  {
    id: 'loss-tags',
    label: 'Loss tags',
    slice: 'P8-13',
    load: () => import('../components/LossTagsTab').then((m) => ({ default: m.LossTagsTab })),
  },
] as const satisfies readonly TabEntry[];

/** The ids of the registry, checked by the compiler wherever a tab is named. */
export type TabId = (typeof entries)[number]['id'];

/** The tab shown without `tab=` in the address. */
export const DEFAULT_TAB: TabId = 'overview';

export interface LearningTab extends Omit<TabEntry<TabId>, 'load'> {
  Component: ComponentType<TabProps>;
}

export const tabs: readonly LearningTab[] = entries.map(({ load, ...tab }): LearningTab => ({
  ...tab,
  Component: lazy(load),
}));

/** Tabs that are planned and not built: named on the Overview, never rendered as tabs. Remove an entry when its tab is added. */
export const plannedTabs: readonly { label: string; slice: string }[] = [];

export const plannedText = (): string =>
  `Not built yet: ${plannedTabs.map((tab) => `${tab.label} (${tab.slice})`).join(', ')}`;
