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
  /** A dimension whose reason the server states (the 422 of a lens by it). */
  dimension?: 'regime' | 'account';
  /** Where the answer lives instead, when it does. */
  link?: { to: string; label: string };
}

/** The reports. A later slice changes one entry here and adds a panel. */
export const reportCatalogue: readonly CatalogueEntry[] = [
  { id: 'overview', label: 'Overview', slice: 'P8-08', status: 'built', reason: '', view: 'overview' },
  { id: 'what-works', label: 'What works', slice: 'P8-08', status: 'built', reason: '', view: 'what-works' },
  { id: 'baskets', label: 'Basket comparison', slice: 'P8-09', status: 'built', reason: '', view: 'baskets' },
  { id: 'pivot', label: 'Pivot builder', slice: 'P8-09', status: 'built', reason: '', view: 'pivot' },
  { id: 'strategies', label: 'Strategies', slice: 'P8-10', status: 'built', reason: '', view: 'strategies' },
  { id: 'side', label: 'Side analysis', slice: 'P8-10', status: 'built', reason: '', view: 'side' },
  { id: 'calendar', label: 'Calendar and time of day', slice: 'P8-10', status: 'built', reason: '', view: 'calendar' },
  { id: 'maemfe', label: 'MAE and MFE', slice: 'P8-10', status: 'built', reason: '', view: 'maemfe' },
  {
    id: 'instruments',
    label: 'Instrument scorecard',
    slice: 'P8-10',
    status: 'built',
    reason: '',
    view: 'instruments',
  },
  { id: 'distribution', label: 'R distribution', slice: 'P8-10', status: 'built', reason: '', view: 'distribution' },
  { id: 'sectors', label: 'Symbol and sector', slice: 'P8-10', status: 'built', reason: '', view: 'sectors' },
  { id: 'exits', label: 'Exit comparison', slice: 'P8-10', status: 'built', reason: '', view: 'exits' },
  { id: 'montecarlo', label: 'Monte Carlo risk', slice: 'P8-10', status: 'built', reason: '', view: 'montecarlo' },
  {
    id: 'regime',
    label: 'Regime',
    slice: 'P3-01d and P3-01e',
    status: 'unavailable',
    reason: 'No market regime is stored with the trades.',
    dimension: 'regime',
  },
  {
    id: 'calibration',
    label: 'Calibration',
    slice: 'a later phase',
    status: 'unavailable',
    reason: 'No champion model is stored, so there is no stated probability to calibrate.',
  },
  {
    id: 'selector',
    label: 'Selector effectiveness',
    slice: 'P8-04',
    status: 'unavailable',
    reason: 'Read it with the decisions on the Learning screen.',
    link: { to: '/learning?tab=decisions', label: 'Open Learning, Decisions' },
  },
  {
    id: 'decisions',
    label: 'Decisions',
    slice: 'P8-04',
    status: 'unavailable',
    reason: 'The decisions report lives on the Learning screen.',
    link: { to: '/learning?tab=decisions', label: 'Open Learning, Decisions' },
  },
  {
    id: 'live',
    label: 'Backtest, paper and live',
    slice: 'P10',
    status: 'unavailable',
    reason: 'No paper trade is stored yet, and no fill.',
  },
  {
    id: 'discipline',
    label: 'Discipline',
    slice: 'P10',
    status: 'unavailable',
    reason: 'No paper trade or fill is stored to compare with the plan.',
  },
  {
    id: 'execution',
    label: 'Execution gap',
    slice: 'P11',
    status: 'unavailable',
    reason: 'No fill is stored, so the gap between the plan and the fill cannot be measured.',
  },
  {
    id: 'account',
    label: 'Account comparison',
    slice: 'P8-04',
    status: 'unavailable',
    reason: 'A simulated trade has no account.',
    dimension: 'account',
  },
];
