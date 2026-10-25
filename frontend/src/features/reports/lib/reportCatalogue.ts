import type { ReportView } from './reportFilter';

export interface CatalogueEntry {
  id: string;
  label: string;
  /** The slice that builds (or built) it. */
  slice: string;
  status: 'built' | 'planned' | 'unavailable';
  reason: string;
  /** The `view=` it opens when built. */
  view?: ReportView;
}

/** The reports. A later slice changes one entry here and adds a panel. */
export const reportCatalogue: readonly CatalogueEntry[] = [
  { id: 'overview', label: 'Overview', slice: 'P8-08', status: 'built', reason: '', view: 'overview' },
  { id: 'what-works', label: 'What works', slice: 'P8-08', status: 'built', reason: '', view: 'what-works' },
  { id: 'baskets', label: 'Basket comparison', slice: 'P8-09', status: 'built', reason: '', view: 'baskets' },
  { id: 'pivot', label: 'Pivot builder', slice: 'P8-09', status: 'built', reason: '', view: 'pivot' },
  { id: 'strategies', label: 'Strategies', slice: 'P8-10', status: 'planned', reason: 'Not built yet.' },
  { id: 'side', label: 'Side analysis', slice: 'P8-10', status: 'planned', reason: 'Not built yet.' },
  { id: 'calendar', label: 'Calendar and time of day', slice: 'P8-10', status: 'planned', reason: 'Not built yet.' },
  { id: 'maemfe', label: 'MAE and MFE', slice: 'P8-10', status: 'planned', reason: 'Not built yet.' },
  { id: 'instruments', label: 'Instrument scorecard', slice: 'P8-10', status: 'planned', reason: 'Not built yet.' },
  { id: 'exits', label: 'Exit comparison', slice: 'P8-10', status: 'planned', reason: 'Not built yet.' },
  {
    id: 'regime',
    label: 'Regime',
    slice: 'P8-10',
    status: 'unavailable',
    reason: 'No market regime is stored with the trades.',
  },
];
