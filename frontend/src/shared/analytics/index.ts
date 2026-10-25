// The shared analytics kit (docs/specs/P8-01): the honest states of a metric, the evidence beside a number, the verdict
// chip, report formatters. Imported by path from lazy screens (`@/shared/analytics`), not through `shared/ui`, so the
// initial bundle does not carry it. `shared/` never imports a feature.
export { BarList, type BarRow } from './BarList';
export { ColumnBars, type ColumnBin } from './ColumnBars';
export { DataTable, type Column, type TableStatus } from './DataTable';
export { EvidenceBlock, type EvidenceProps } from './EvidenceBlock';
export {
  formatAsOf,
  formatAsOfDay,
  formatCount,
  formatR,
  formatRange,
  formatRate,
  formatRatio,
  formatSigned,
  NOT_AVAILABLE,
} from './format';
export { carriesNumber, metricOf, nonValueKinds, resolveMetric, type MetricKind, type MetricState } from './metric';
export { HATCH_ALPHA, HeatGrid, type HeatGridCell, type HeatGridColumn, type HeatGridRow } from './HeatGrid';
export { MetricCell, type MetricCellProps } from './MetricCell';
export { MetricTile } from './MetricTile';
export { MetricValue, type MetricValueProps } from './MetricValue';
export { NonValue } from './NonValue';
export { useUrlState } from './useUrlState';
export { VerdictChip, type Verdict } from './VerdictChip';
