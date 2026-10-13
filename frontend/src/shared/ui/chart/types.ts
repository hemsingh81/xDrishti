/**
 * Theme colour of a series: a role, not a value. The chart resolves it against the active MUI theme, so a colour-mode
 * switch re-colours the lines and no component names a hex code.
 */
export type ChartColor = 'accent' | 'neutral' | 'info' | 'good' | 'warn' | 'bad';

/** Line style. It differs per series, so the lines are told apart without relying on the colour. */
export type ChartDash = 'solid' | 'dashed' | 'dotted';

/** A calendar date (yyyy-MM-dd) and the value on it; null = no value that day (the line breaks). */
export type ChartPoint = readonly [date: string, value: number | null];

export interface ChartSeries {
  /** Stable id: keeps the legend state and the zoom when the data changes. */
  id: string;
  /** Shown in the legend, the tooltip and the table. */
  name: string;
  color: ChartColor;
  dash?: ChartDash;
  /** In date order. */
  data: readonly ChartPoint[];
}

/** The option object handed to the chart library; built by `buildChartOption`, typed loosely on purpose. */
export type ChartOption = Record<string, unknown>;

/** Where the pointer is on the chart: the index of the nearest date, the x of that date in px and the chart's width. */
export interface ChartHover {
  index: number;
  x: number;
  width: number;
}
