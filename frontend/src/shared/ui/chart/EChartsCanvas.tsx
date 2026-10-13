// The ONLY module that imports the chart library (an ESLint rule keeps it so). TimeSeriesChart loads it with a dynamic
// import, so the library is its own chunk and not part of the first page load. Imports are by feature: the line chart,
// the grid, the axis pointer, inside zoom and the canvas renderer, nothing else. The tooltip, the legend and the
// description are plain React next to the canvas (and use the theme and the formatters directly), so the library's
// tooltip (~8 KB gzip), legend and aria components are not registered: the chunk stays within its budget.
import { LineChart } from 'echarts/charts';
import { AxisPointerComponent, DataZoomInsideComponent, GridComponent } from 'echarts/components';
import { init, use as register, type EChartsCoreOption, type EChartsType } from 'echarts/core';
import { CanvasRenderer } from 'echarts/renderers';
import { Box } from '@mui/material';
import { useEffect, useEffectEvent, useRef } from 'react';
import type { ChartHover, ChartOption } from './types';

register([LineChart, GridComponent, AxisPointerComponent, DataZoomInsideComponent, CanvasRenderer]);

export interface EChartsCanvasProps {
  option: ChartOption;
  /** When this changes (other data), the zoom is reset. */
  resetKey: string;
  /** Each change asks for the zoom to be reset. */
  resetToken: number;
  onZoomChange: (zoomed: boolean) => void;
  /** The pointer moved over a date (or left the chart: null). */
  onHover: (hover: ChartHover | null) => void;
}

/** Is a zoom in force? `getOption()` is typed loosely by the library, so it is read defensively. */
function isZoomed(chart: EChartsType): boolean {
  const zoom: unknown = (chart.getOption() as { dataZoom?: unknown }).dataZoom;
  const first: unknown = Array.isArray(zoom) ? zoom[0] : undefined;
  if (typeof first !== 'object' || first === null) return false;
  const { start, end } = first as { start?: unknown; end?: unknown };
  return (typeof start === 'number' && start > 0) || (typeof end === 'number' && end < 100);
}

/** The date index an `updateAxisPointer` event points at, or null when the pointer is off the grid. */
function hoveredIndex(event: unknown): number | null {
  const info: unknown = (event as { axesInfo?: unknown }).axesInfo;
  const first: unknown = Array.isArray(info) ? info[0] : undefined;
  const value = (first as { value?: unknown } | undefined)?.value;
  return typeof value === 'number' ? Math.round(value) : null;
}

/** One ECharts line chart that fills its parent, follows its size and is disposed with it. */
export default function EChartsCanvas({ option, resetKey, resetToken, onZoomChange, onHover }: EChartsCanvasProps) {
  const host = useRef<HTMLDivElement>(null);
  const chart = useRef<EChartsType | null>(null);
  const zoomChanged = useEffectEvent(onZoomChange);
  const hovered = useEffectEvent(onHover);

  useEffect(() => {
    const element = host.current;
    if (!element) return;
    const instance = init(element, undefined, { renderer: 'canvas' });
    chart.current = instance;
    instance.on('datazoom', () => {
      zoomChanged(isZoomed(instance));
    });
    instance.on('updateAxisPointer', (event) => {
      const index = hoveredIndex(event);
      const x = index === null ? null : instance.convertToPixel({ xAxisIndex: 0 }, index);
      hovered(index !== null && typeof x === 'number' ? { index, x, width: instance.getWidth() } : null);
    });
    instance.on('globalout', () => {
      hovered(null);
    });
    const observer =
      typeof ResizeObserver === 'undefined'
        ? null
        : new ResizeObserver(() => {
            instance.resize();
          });
    observer?.observe(element);
    return () => {
      observer?.disconnect();
      instance.dispose();
      chart.current = null;
    };
  }, []);

  useEffect(() => {
    // Lines come and go with the legend: `replaceMerge` drops the ones no longer in the option and keeps the zoom.
    chart.current?.setOption(option as EChartsCoreOption, { replaceMerge: ['series'] });
  }, [option]);

  useEffect(() => {
    chart.current?.dispatchAction({ type: 'dataZoom', start: 0, end: 100 });
    zoomChanged(false);
  }, [resetKey, resetToken]);

  // `touch-action: pan-y`: a vertical swipe over the chart still scrolls the page; a sideways drag or a pinch is the chart's.
  return <Box ref={host} aria-hidden sx={{ width: '100%', height: '100%', touchAction: 'pan-y' }} />;
}
