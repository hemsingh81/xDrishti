import { formatIstDayMonth, formatIstTime, formatPrice } from '@/shared/lib/format';
import type { Candle, PriceBar, PriceEvent, PriceLine, PriceMarker } from './priceChartTypes';

/** Candles for the library, oldest first. */
export const toCandles = (bars: readonly PriceBar[]): Candle[] =>
  bars.map((b) => ({ time: b.t, open: b.o, high: b.h, low: b.l, close: b.c }));

/** A level is drawn only for a real positive price: a missing target is no line, never a line at zero. */
export const drawableLines = (lines: readonly PriceLine[]): PriceLine[] =>
  lines.filter((line) => Number.isFinite(line.price) && line.price > 0);

/** The seconds between two bars (the smallest gap), or 60 for a single bar. */
export function barSpan(bars: readonly PriceBar[]): number {
  let span = Infinity;
  for (let i = 1; i < bars.length; i++) {
    const gap = (bars[i]?.t ?? 0) - (bars[i - 1]?.t ?? 0);
    if (gap > 0 && gap < span) span = gap;
  }
  return Number.isFinite(span) ? span : 60;
}

/** The start of the bar that holds the instant, or `null` when the instant is before the first bar or after the last one ends. */
export function snapToBar(bars: readonly PriceBar[], t: number, span: number = barSpan(bars)): number | null {
  const first = bars[0];
  const last = bars[bars.length - 1];
  if (!first || !last || t < first.t || t >= last.t + span) return null;
  let low = 0;
  let high = bars.length - 1;
  while (low < high) {
    const mid = (low + high + 1) >>> 1;
    if ((bars[mid]?.t ?? Infinity) <= t) low = mid;
    else high = mid - 1;
  }
  return bars[low]?.t ?? null;
}

/**
 * Shape and place of each event: entry is an arrow in the direction of the trade, an add a circle, an exit a square;
 * entries sit on the side of the bar the trade is taken from, exits on the other. The text names the kind and the share.
 */
export function buildMarkers(
  events: readonly PriceEvent[],
  bars: readonly PriceBar[],
): { markers: PriceMarker[]; outside: PriceEvent[] } {
  const span = barSpan(bars);
  const markers: PriceMarker[] = [];
  const outside: PriceEvent[] = [];
  for (const event of events) {
    const time = snapToBar(bars, event.t, span);
    if (time === null) {
      outside.push(event);
      continue;
    }
    const buy = event.side === 'BUY';
    const entering = event.kind !== 'exit';
    markers.push({
      id: event.id,
      time,
      position: entering === buy ? 'belowBar' : 'aboveBar',
      shape: event.kind === 'entry' ? (buy ? 'arrowUp' : 'arrowDown') : event.kind === 'add' ? 'circle' : 'square',
      text: event.text,
    });
  }
  markers.sort((a, b) => a.time - b.time);
  return { markers, outside };
}

/** "14:35" in India time. */
export const clock = (t: number): string => formatIstTime(t * 1000);

/** "12 Oct 14:35" in India time: the crosshair label. */
export const dateClock = (t: number): string => `${formatIstDayMonth(t * 1000)} ${clock(t)}`;

/** The axis label: the clock inside a session, the day at a new day or on the day timeframe (`dayOnly`). */
export function tickLabel(t: number, dayOnly: boolean): string {
  return dayOnly || clock(t) === '09:15' ? formatIstDayMonth(t * 1000) : clock(t);
}

/** The accessible description of the picture: what is drawn, in words. */
export function priceSummary(
  label: string,
  bars: readonly PriceBar[],
  lines: readonly PriceLine[],
  events: readonly PriceEvent[],
): string {
  const first = bars[0];
  const last = bars[bars.length - 1];
  if (!first || !last) return `${label}: no bars.`;
  const high = Math.max(...bars.map((b) => b.h));
  const low = Math.min(...bars.map((b) => b.l));
  const levels = drawableLines(lines)
    .map((l) => l.title)
    .join(', ');
  return `${label}: ${String(bars.length)} bars from ${dateClock(first.t)} to ${dateClock(last.t)} IST, low ${formatPrice(low)}, high ${formatPrice(high)}${levels ? `; levels ${levels}` : ''}; ${String(events.length)} ${events.length === 1 ? 'event' : 'events'}. The table below has the same figures.`;
}
