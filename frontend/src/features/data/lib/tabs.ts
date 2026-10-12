import { isActionsView } from './actions';
import { isGapsView } from './gaps';

/** The tabs under the pipeline strip. `?tab=` names one; coverage is the default and is not written to the URL. */
export const dataTabs = ['coverage', 'calendar', 'actions', 'gaps', 'runs'] as const;
export type DataTab = (typeof dataTabs)[number];

export const defaultDataTab: DataTab = 'coverage';

export const dataTabLabels: Readonly<Record<DataTab, string>> = {
  coverage: 'Coverage',
  calendar: 'Calendar',
  actions: 'Corporate actions',
  gaps: 'Gaps',
  runs: 'Runs',
};

export const isDataTab = (value: string | null): value is DataTab => dataTabs.some((tab) => tab === value);

/** The anchor the corporate actions had as a card; it opens the tab (and is the id of its panel). */
export const actionsAnchor = '#corporate-actions';

export const tabId = (tab: DataTab) => `data-tab-${tab}`;
export const panelId = (tab: DataTab) => (tab === 'actions' ? actionsAnchor.slice(1) : `data-panel-${tab}`);

/**
 * The tab a URL shows. `?tab=` wins. Links made before the tabs existed still land on the right one: the bell's
 * `?section=actions&actions=suggested` (and any `?actions=`, or `#corporate-actions`) opens the corporate actions, a
 * `?gaps=open|unfillable|filled|all` the gaps; everything else, `?id=<instrument>` included, the coverage.
 */
export function resolveTab(params: URLSearchParams, hash = ''): DataTab {
  const named = params.get('tab');
  if (isDataTab(named)) return named;
  if (params.get('section') === 'actions' || isActionsView(params.get('actions')) || hash === actionsAnchor) {
    return 'actions';
  }
  if (isGapsView(params.get('gaps'))) return 'gaps';
  return defaultDataTab;
}
