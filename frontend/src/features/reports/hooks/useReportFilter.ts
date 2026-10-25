import { useUrlState } from '@/shared/analytics';
import { readFilter, views, type FilterPatch, type ReportFilter, type ReportView } from '../lib/reportFilter';

export interface ReportUrl {
  view: ReportView;
  filter: ReportFilter;
  /** Changes address parameters in one history entry (`null` removes one). */
  set: (patch: FilterPatch) => void;
}

/** The view and the filters, read from the address (a link reproduces the screen; Back works). */
export function useReportFilter(): ReportUrl {
  const url = useUrlState();
  return { view: url.choice('view', views, 'overview'), filter: readFilter(url.text), set: url.update };
}
