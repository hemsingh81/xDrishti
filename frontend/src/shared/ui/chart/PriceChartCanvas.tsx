import {
  CandlestickSeries,
  ColorType,
  createChart,
  createSeriesMarkers,
  LineStyle,
  type IChartApi,
  type IPriceLine,
  type ISeriesApi,
  type ISeriesMarkersPluginApi,
  type Time,
  type UTCTimestamp,
} from 'lightweight-charts';
import { useEffect, useRef } from 'react';
import { dateClock, tickLabel } from './priceChartModel';
import type { Candle, PriceChartColors, PriceFocus, PriceLine, PriceMarker } from './priceChartTypes';

export interface PriceChartCanvasProps {
  candles: readonly Candle[];
  lines: readonly PriceLine[];
  markers: readonly PriceMarker[];
  colors: PriceChartColors;
  height: number;
  /** The day timeframe labels its axis with days. */
  dayOnly: boolean;
  /** Where the event buttons put the crosshair (`null`: nowhere). */
  focus: PriceFocus | null;
}

const asTime = (t: number) => t as UTCTimestamp;

/**
 * The only module that imports the chart library (TradingView Lightweight Charts, Apache-2.0; ESLint and boundaries.test.ts keep it so).
 * Loaded lazily by `PriceChart`. Up candles are hollow and down candles filled (never colour alone); every level has its title on the
 * axis; times are shown in India time by formatters, the data is never shifted. The attribution logo is on (the licence's link duty).
 */
export default function PriceChartCanvas({
  candles,
  lines,
  markers,
  colors,
  height,
  dayOnly,
  focus,
}: PriceChartCanvasProps) {
  const host = useRef<HTMLDivElement>(null);
  const chart = useRef<IChartApi | null>(null);
  const series = useRef<ISeriesApi<'Candlestick'> | null>(null);
  const markerApi = useRef<ISeriesMarkersPluginApi<Time> | null>(null);
  const drawn = useRef<IPriceLine[]>([]);

  useEffect(() => {
    const element = host.current;
    if (!element) return;
    const created = createChart(element, {
      autoSize: true,
      layout: {
        background: { type: ColorType.Solid, color: colors.background },
        textColor: colors.text,
        attributionLogo: true,
      },
      grid: { vertLines: { color: colors.grid }, horzLines: { color: colors.grid } },
      rightPriceScale: { borderColor: colors.border },
      timeScale: {
        borderColor: colors.border,
        timeVisible: true,
        secondsVisible: false,
        tickMarkFormatter: (time: Time) => tickLabel(Number(time), dayOnly),
      },
      localization: { timeFormatter: (time: Time) => `${dateClock(Number(time))} IST` },
    });
    const candlesSeries = created.addSeries(CandlestickSeries, {
      upColor: 'rgba(0,0,0,0)',
      borderUpColor: colors.up,
      wickUpColor: colors.up,
      downColor: colors.down,
      borderDownColor: colors.down,
      wickDownColor: colors.down,
    });
    chart.current = created;
    series.current = candlesSeries;
    markerApi.current = createSeriesMarkers(candlesSeries, []);
    return () => {
      created.remove();
      chart.current = null;
      series.current = null;
      markerApi.current = null;
      drawn.current = [];
    };
  }, [colors, dayOnly]);

  useEffect(() => {
    const s = series.current;
    if (!s) return;
    s.setData(candles.map((c) => ({ ...c, time: asTime(c.time) })));
    for (const line of drawn.current) s.removePriceLine(line);
    drawn.current = lines.map((line) =>
      s.createPriceLine({
        price: line.price,
        color: colors[line.kind],
        lineWidth: 1,
        lineStyle: line.kind === 'entry' ? LineStyle.Dotted : LineStyle.Dashed,
        axisLabelVisible: true,
        title: line.title,
      }),
    );
    markerApi.current?.setMarkers(
      markers.map((m) => ({
        time: asTime(m.time),
        position: m.position,
        shape: m.shape,
        text: m.text,
        color: m.shape === 'square' ? colors.down : colors.entry,
      })),
    );
    chart.current?.timeScale().fitContent();
  }, [candles, lines, markers, colors, dayOnly]);

  useEffect(() => {
    const c = chart.current;
    const s = series.current;
    if (!c || !s) return;
    if (focus) c.setCrosshairPosition(focus.price, asTime(focus.time), s);
    else c.clearCrosshairPosition();
  }, [focus, candles, dayOnly, colors]);

  return <div ref={host} style={{ height, width: '100%' }} />;
}
