import { Alert, Box, Button, Stack, useMediaQuery } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { lazy, Suspense, useCallback, useId, useMemo, useRef, useState, type ReactNode } from 'react';
import { formatDate } from '@/shared/lib/format';
import { useInView } from '@/shared/lib/useInView';
import { stateAlpha } from '@/theme';
import { ChartBoundary } from './ChartBoundary';
import { ChartSkeleton } from './ChartSkeleton';
import { alignedValues, axisDateLabel, chartDates } from './chartData';
import { buildChartOption } from './chartOption';
import { chartSummary } from './chartSummary';
import { chartTokens } from './chartTheme';
import { ChartLegend } from './ChartLegend';
import { ChartTable } from './ChartTable';
import { ChartTooltip } from './ChartTooltip';
import type { ChartHover, ChartSeries } from './types';

// The chart library is a separate chunk, fetched only when a chart is about to be shown. A failed fetch is remembered by
// `lazy`, so "Try again" swaps in a fresh lazy component.
const loadCanvas = () => import('./EChartsCanvas');
const canvas = { Component: lazy(loadCanvas) };

export interface TimeSeriesChartProps {
  /** What the chart shows ("Portfolio value history"): its accessible name and the table's. */
  label: string;
  series: readonly ChartSeries[];
  /** Tooltip, summary and table: "₹2,03,400". */
  valueFormatter: (value: number) => string;
  /** The y axis, shorter than the tooltip: "₹2L". Defaults to `valueFormatter`. */
  axisFormatter?: (value: number) => string;
  /** "13 Oct 2026". */
  dateFormatter?: (date: string) => string;
  /** Height of the chart area in px; the loading skeleton has the same, so nothing shifts. */
  height?: number;
  loading?: boolean;
  /** Shown in the chart area when no series has a point. */
  empty?: ReactNode;
  /** Old data is on screen while new data loads: the chart is dimmed. */
  busy?: boolean;
  /** Load the chart library only when the chart is about to scroll into view. */
  deferUntilVisible?: boolean;
}

/**
 * A line chart over dates, for money-over-time views. ECharts draws it on a canvas (lazy chunk, theme colours, no motion
 * when the visitor asks for less); everything the canvas cannot offer is plain HTML next to it: the accessible summary,
 * the legend as toggle buttons, and "Show as table" with the same figures.
 */
export function TimeSeriesChart({
  label,
  series,
  valueFormatter,
  axisFormatter = valueFormatter,
  dateFormatter = formatDate,
  height = 300,
  loading = false,
  empty,
  busy = false,
  deferUntilVisible = false,
}: TimeSeriesChartProps) {
  const theme = useTheme();
  const tokens = useMemo(() => chartTokens(theme), [theme]);
  const reducedMotion = useMediaQuery('(prefers-reduced-motion: reduce)');
  const root = useRef<HTMLDivElement>(null);
  const near = useInView(root, { enabled: deferUntilVisible });
  const tableId = useId();

  const [hidden, setHidden] = useState<ReadonlySet<string>>(new Set());
  const [tableOpen, setTableOpen] = useState(false);
  const [failed, setFailed] = useState(false);
  const [zoomed, setZoomed] = useState(false);
  const [resetToken, setResetToken] = useState(0);
  const [hover, setHover] = useState<ChartHover | null>(null);

  const visible = useMemo(() => series.filter((s) => !hidden.has(s.id)), [series, hidden]);
  const dates = useMemo(() => chartDates(visible), [visible]);
  const allDates = useMemo(() => chartDates(series), [series]);
  const hasData = allDates.length > 0;
  const summary = useMemo(
    () => chartSummary({ label, series: visible, valueFormatter, dateFormatter }),
    [label, visible, valueFormatter, dateFormatter],
  );
  const option = useMemo(
    () =>
      buildChartOption({
        series: visible,
        dates,
        tokens,
        axisFormatter,
        axisDateLabel: axisDateLabel(dates),
        animate: !reducedMotion,
      }),
    [visible, dates, tokens, axisFormatter, reducedMotion],
  );

  const toggle = (id: string) => {
    setHidden((previous) => {
      const next = new Set(previous);
      if (!next.delete(id)) next.add(id);
      return next;
    });
  };
  // The pointer's date and the values on it; only a change of date (or width) renders again.
  const onHover = useCallback((next: ChartHover | null) => {
    setHover((previous) => (previous?.index === next?.index && previous?.width === next?.width ? previous : next));
  }, []);
  const hoveredDate = hover ? dates[hover.index] : undefined;
  const aligned = useMemo(() => visible.map((s) => alignedValues(s, dates)), [visible, dates]);
  const showTable = tableOpen || failed;
  // The zoom resets when other data arrives, not when a line is hidden: the key is the extent of all the data.
  const resetKey = `${allDates[0] ?? ''}|${allDates[allDates.length - 1] ?? ''}|${String(allDates.length)}`;
  const [attempt, setAttempt] = useState(0);
  const tryAgain = () => {
    canvas.Component = lazy(loadCanvas);
    setFailed(false);
    setAttempt((n) => n + 1);
  };
  const EChartsCanvas = canvas.Component;

  let area: ReactNode;
  if (loading) area = <ChartSkeleton height={height} />;
  else if (!hasData) area = empty;
  else if (!near) area = <ChartSkeleton height={height} />;
  else {
    area = (
      <ChartBoundary
        key={attempt}
        fallback={
          <Alert
            severity="warning"
            action={
              <Button color="inherit" size="small" onClick={tryAgain}>
                Try again
              </Button>
            }
          >
            The chart could not be loaded. The table below has the same figures.
          </Alert>
        }
        onError={() => {
          setFailed(true);
        }}
      >
        <Suspense fallback={<ChartSkeleton height={height} />}>
          <Box role="img" aria-label={summary} sx={{ height, position: 'relative' }}>
            <EChartsCanvas
              option={option}
              resetKey={resetKey}
              resetToken={resetToken}
              onZoomChange={setZoomed}
              onHover={onHover}
            />
            {hover && hoveredDate !== undefined && (
              <ChartTooltip
                date={dateFormatter(hoveredDate)}
                rows={visible.map((s, i) => ({ series: s, value: aligned[i]?.[hover.index] ?? null }))}
                x={hover.x}
                width={hover.width}
                colors={tokens.colors}
                valueFormatter={valueFormatter}
              />
            )}
          </Box>
        </Suspense>
      </ChartBoundary>
    );
  }

  return (
    <Box ref={root}>
      <Box
        aria-busy={busy || undefined}
        sx={{ minHeight: height, opacity: busy ? stateAlpha.busy : 1, transition: 'opacity 120ms' }}
      >
        {area}
      </Box>
      {hasData && !loading && (
        <Stack
          direction="row"
          sx={{ mt: 1, flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', gap: 1 }}
        >
          <ChartLegend series={series} hidden={hidden} colors={tokens.colors} onToggle={toggle} />
          <Stack direction="row" sx={{ gap: 0.5 }}>
            {zoomed && !failed && (
              <Button
                size="small"
                onClick={() => {
                  setResetToken((n) => n + 1);
                }}
              >
                Reset zoom
              </Button>
            )}
            <Button
              size="small"
              aria-expanded={showTable}
              aria-controls={tableId}
              disabled={failed}
              onClick={() => {
                setTableOpen((open) => !open);
              }}
            >
              {showTable ? 'Hide table' : 'Show as table'}
            </Button>
          </Stack>
        </Stack>
      )}
      {hasData && !loading && showTable && (
        <ChartTable
          id={tableId}
          label={label}
          dates={dates}
          series={visible}
          valueFormatter={valueFormatter}
          dateFormatter={dateFormatter}
        />
      )}
    </Box>
  );
}
