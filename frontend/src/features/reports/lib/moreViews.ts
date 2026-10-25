import type { ReportView } from './reportFilter';

/** The views of the P8-10 reports (each is one lazy-loaded screen body of `MoreReport`). */
const moreViews: ReadonlySet<ReportView> = new Set<ReportView>([
  'strategies',
  'side',
  'calendar',
  'maemfe',
  'distribution',
  'instruments',
  'sectors',
]);

export const isMoreReport = (view: ReportView): boolean => moreViews.has(view);
