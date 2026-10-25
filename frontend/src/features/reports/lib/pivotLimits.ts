/** The most baskets one comparison takes: the chart has six colour roles (P8-09 decision 2). */
export const MAX_BASKETS = 6;
/** The pivot cap of the server (cells); a bigger pivot is refused with the server's own message. */
export const MAX_PIVOT_CELLS = 2000;
/** Rows past which the pivot grid scrolls inside its card (it never truncates). */
export const TALL_GRID_ROWS = 40;

/** The dimensions the built lens whitelist offers (sector and hour join when P8-10 adds them). */
export const pivotDimensions = [
  { id: 'basket', label: 'Basket' },
  { id: 'instrument', label: 'Instrument' },
  { id: 'strategy', label: 'Strategy' },
  { id: 'timeframe', label: 'Timeframe' },
  { id: 'side', label: 'Side' },
  { id: 'style', label: 'Style' },
  { id: 'exit', label: 'Exit' },
  { id: 'month', label: 'Month' },
  { id: 'weekday', label: 'Weekday' },
] as const;
export type PivotDimension = (typeof pivotDimensions)[number]['id'];
export const dimensionLabel = (id: string): string => pivotDimensions.find((d) => d.id === id)?.label ?? id;

/** Dimensions the server refuses, asked once for the reason. */
export const refusedDimensions = ['regime', 'account'] as const;

/** The metrics of the pivot (snake-case wire names) in words. */
export const pivotMetrics = [
  { id: 'expectancy_r', label: 'Expectancy (R per trade)' },
  { id: 'expectancy_lower_r', label: 'Expectancy lower bound (R)' },
  { id: 'total_r', label: 'Total R' },
  { id: 'win_rate', label: 'Win rate' },
  { id: 'profit_factor', label: 'Profit factor' },
  { id: 'max_drawdown_r', label: 'Max drawdown (R)' },
  { id: 'trades', label: 'Trades' },
] as const;
export type PivotMetric = (typeof pivotMetrics)[number]['id'];

export const DEFAULT_ROWS: PivotDimension = 'strategy';
export const DEFAULT_COLUMNS: PivotDimension = 'timeframe';
export const DEFAULT_METRIC: PivotMetric = 'expectancy_r';
