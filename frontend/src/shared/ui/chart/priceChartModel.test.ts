import { describe, expect, it } from 'vitest';
import { barSpan, buildMarkers, drawableLines, priceSummary, snapToBar, tickLabel, toCandles } from './priceChartModel';
import type { PriceBar, PriceEvent, PriceLine } from './priceChartTypes';

const base = Date.UTC(2026, 2, 2, 3, 45) / 1000; // 09:15 IST
const bars: PriceBar[] = Array.from({ length: 6 }, (_, i) => ({
  t: base + i * 300,
  o: 100,
  h: 101,
  l: 99,
  c: 100,
  v: 10,
}));
const event = (over: Partial<PriceEvent>): PriceEvent => ({
  id: 'e',
  t: base + 600,
  price: 100,
  kind: 'entry',
  side: 'BUY',
  text: 'Entry 100%',
  announce: 'Entry',
  ...over,
});

describe('price chart model', () => {
  it('maps bars to candles and finds the span', () => {
    expect(toCandles(bars)[1]).toEqual({ time: base + 300, open: 100, high: 101, low: 99, close: 100 });
    expect(barSpan(bars)).toBe(300);
    expect(barSpan(bars.slice(0, 1))).toBe(60);
  });

  it('snaps an instant to the bar that holds it and refuses one outside the window', () => {
    expect(snapToBar(bars, base + 659)).toBe(base + 600);
    expect(snapToBar(bars, base + 1800 - 1)).toBe(base + 1500);
    expect(snapToBar(bars, base - 1)).toBeNull();
    expect(snapToBar(bars, base + 1800)).toBeNull();
    expect(snapToBar([], base)).toBeNull();
  });

  it('draws a level only for a positive price: no target is no line, never a line at zero', () => {
    const lines: PriceLine[] = [
      { id: 'a', kind: 'stop', price: 98, title: 'Stop 98.00' },
      { id: 'b', kind: 'target', price: 0, title: 'Target 0.00' },
      { id: 'c', kind: 'target', price: Number.NaN, title: 'Target' },
    ];
    expect(drawableLines(lines).map((l) => l.id)).toEqual(['a']);
  });

  it('gives each event kind its shape and puts entries and exits on opposite sides of the bar', () => {
    const { markers } = buildMarkers(
      [
        event({ id: 'in', kind: 'entry' }),
        event({ id: 'add', kind: 'add', t: base + 900 }),
        event({ id: 'out', kind: 'exit', t: base + 1200, text: 'Exit 100%' }),
      ],
      bars,
    );
    expect(markers.map((m) => [m.id, m.shape, m.position])).toEqual([
      ['in', 'arrowUp', 'belowBar'],
      ['add', 'circle', 'belowBar'],
      ['out', 'square', 'aboveBar'],
    ]);
    const sell = buildMarkers(
      [event({ side: 'SELL' }), event({ id: 'x', side: 'SELL', kind: 'exit', t: base + 900 })],
      bars,
    ).markers;
    expect(sell.map((m) => [m.shape, m.position])).toEqual([
      ['arrowDown', 'aboveBar'],
      ['square', 'belowBar'],
    ]);
  });

  it('flags an event outside the bars instead of drawing it', () => {
    const { markers, outside } = buildMarkers([event({ id: 'late', t: base + 100_000 }), event({ id: 'ok' })], bars);
    expect(markers.map((m) => m.id)).toEqual(['ok']);
    expect(outside.map((e) => e.id)).toEqual(['late']);
  });

  it('marker count is the event count when every event is inside the window', () => {
    for (let n = 0; n <= 6; n++) {
      const events = Array.from({ length: n }, (_, i) => event({ id: String(i), t: base + i * 300 }));
      expect(buildMarkers(events, bars).markers).toHaveLength(n);
    }
  });

  it('labels the axis in India time and says what is drawn', () => {
    expect(tickLabel(base + 300, false)).toBe('09:20');
    expect(tickLabel(base, false)).toMatch(/2 Mar/);
    expect(tickLabel(base + 300, true)).toMatch(/2 Mar/);
    const text = priceSummary('Chart', bars, [{ id: 's', kind: 'stop', price: 98, title: 'Stop 98.00' }], [event({})]);
    expect(text).toContain('6 bars');
    expect(text).toContain('Stop 98.00');
    expect(text).toContain('1 event.');
    expect(priceSummary('Chart', [], [], [])).toBe('Chart: no bars.');
  });
});
