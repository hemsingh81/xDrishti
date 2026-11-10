// Public API of the reports feature (Reports screen, P8-08). Other code imports only from here.
export { ReportsPage } from './components/ReportsPageLazy';
export { CellDetailPage, TradeReplayPage } from './components/ReplayPagesLazy';
// The training-simulation report panels (AU-15), drawn by the simulation screen.
export { TrainingBanner } from './components/TrainingBanner';
export { TrainingReport } from './components/TrainingReport';
export { trainingReportTypes, type TrainingReportType } from './lib/trainingReports';
export { useBasketNames } from './hooks/useBasketNames';
export type { ReportQuery } from './lib/reportFilter';
