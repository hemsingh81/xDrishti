// The chart wrapper. Import it by this path (@/shared/ui/chart), not through the shared/ui barrel, so a screen without
// a chart does not pull it in. The chart library itself is loaded lazily by the wrapper.
export { TimeSeriesChart, type TimeSeriesChartProps } from './TimeSeriesChart';
export type { ChartColor, ChartDash, ChartPoint, ChartSeries } from './types';
export { CategoryChart, type CategoryChartProps } from './CategoryChart';
export type { CategoryData, CategorySlice, StackCategory, StackSeries } from './categoryTypes';
export { PriceChart, type PriceChartProps } from './PriceChart';
export { lightweightChartsNotice } from './attribution';
export type { PriceBar, PriceEvent, PriceEventKind, PriceLine, PriceLineKind } from './priceChartTypes';
