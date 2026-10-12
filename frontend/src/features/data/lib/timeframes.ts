/** The timeframes of the bars API: 1-minute bars as stored, the rest derived from them. */
export const timeframes = ['1m', '5m', '10m', '15m', '30m', '1h', '1D', '1W', '1M'] as const;
export type Timeframe = (typeof timeframes)[number];

export const defaultTimeframe: Timeframe = '1m';

export const timeframeLabels: Readonly<Record<Timeframe, string>> = {
  '1m': '1 minute',
  '5m': '5 minutes',
  '10m': '10 minutes',
  '15m': '15 minutes',
  '30m': '30 minutes',
  '1h': '1 hour',
  '1D': '1 day',
  '1W': '1 week',
  '1M': '1 month',
};

export const isTimeframe = (value: string | null): value is Timeframe => timeframes.some((t) => t === value);

/** Bars of these timeframes are stamped with an India midnight: show the day only. */
export const isDayTimeframe = (timeframe: string): boolean =>
  timeframe === '1D' || timeframe === '1W' || timeframe === '1M';
