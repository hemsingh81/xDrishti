// The second module that imports the chart library (an ESLint rule and a boundary test keep it to this and
// EChartsCanvas.tsx). CategoryChart loads it with a dynamic import, so the pie and bar code is its own chunk, fetched only
// when a donut or a stacked bar is about to be shown, and the time-series chunk does not carry it. Imports are by
// feature: the pie, the bar, the grid and the canvas renderer, nothing else. The tooltip, legend and description are
// plain React next to the canvas (as for the line chart), so the library's tooltip and aria components stay out.
import { BarChart, PieChart } from 'echarts/charts';
import { GridComponent } from 'echarts/components';
import { init, use as register, type EChartsCoreOption, type EChartsType } from 'echarts/core';
import { CanvasRenderer } from 'echarts/renderers';
import { Box } from '@mui/material';
import { useEffect, useEffectEvent, useRef } from 'react';
import type { CategoryHover } from './categoryTypes';
import type { ChartOption } from './types';

register([PieChart, BarChart, GridComponent, CanvasRenderer]);

export interface CategoryChartCanvasProps {
  option: ChartOption;
  /** The pointer is over a slice or a bar (or left it: null). */
  onHover: (hover: CategoryHover | null) => void;
}

/** The slice or bar an element event points at, with the pointer position inside the chart. */
function hoverOf(params: unknown, width: number): CategoryHover | null {
  const { dataIndex, event } = params as { dataIndex?: unknown; event?: { offsetX?: unknown; offsetY?: unknown } };
  const x = event?.offsetX;
  const y = event?.offsetY;
  return typeof dataIndex === 'number' && typeof x === 'number' && typeof y === 'number'
    ? { index: dataIndex, x, y, width }
    : null;
}

/** One ECharts donut or stacked bar that fills its parent, follows its size and is disposed with it. */
export default function CategoryChartCanvas({ option, onHover }: CategoryChartCanvasProps) {
  const host = useRef<HTMLDivElement>(null);
  const chart = useRef<EChartsType | null>(null);
  const hovered = useEffectEvent(onHover);

  useEffect(() => {
    const element = host.current;
    if (!element) return;
    const instance = init(element, undefined, { renderer: 'canvas' });
    chart.current = instance;
    instance.on('mouseover', (params) => {
      hovered(hoverOf(params, instance.getWidth()));
    });
    instance.on('mouseout', () => {
      hovered(null);
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
    // Slices and bars come and go with the legend, and a donut turns into a bar chart when the grouping changes:
    // `notMerge` draws the new option from scratch.
    chart.current?.setOption(option as EChartsCoreOption, { notMerge: true });
  }, [option]);

  // `touch-action: pan-y`: a vertical swipe over the chart still scrolls the page.
  return <Box ref={host} aria-hidden sx={{ width: '100%', height: '100%', touchAction: 'pan-y' }} />;
}
