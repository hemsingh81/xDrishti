import { Alert, Box, Button, Stack, useMediaQuery } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { lazy, Suspense, useId, useMemo, useRef, useState, type ReactNode } from 'react';
import { useInView } from '@/shared/lib/useInView';
import { stateAlpha } from '@/theme';
import { buildCategoryOption } from './categoryOption';
import { legendEntries, shareText, sliceTotal, visibleData } from './categoryData';
import { categorySummary } from './categorySummary';
import type { CategoryData, CategoryHover } from './categoryTypes';
import { CategoryLegend, type CategoryLegendEntry } from './CategoryLegend';
import { CategoryTable } from './CategoryTable';
import { CategoryTooltip } from './CategoryTooltip';
import { ChartBoundary } from './ChartBoundary';
import { ChartSkeleton } from './ChartSkeleton';
import { chartTokens } from './chartTheme';

// The pie and bar code is a chunk of its own (not the line chart's), fetched only when a donut or a stacked bar is about
// to be shown. A failed fetch is remembered by `lazy`, so "Try again" swaps in a fresh lazy component.
const loadCanvas = () => import('./CategoryChartCanvas');
const canvas = { Component: lazy(loadCanvas) };

export interface CategoryChartProps {
  /** What the chart shows ("Allocation by sector"): its accessible name and the table's. */
  label: string;
  data: CategoryData;
  /** Tooltip, summary and table: "₹2,03,400". */
  valueFormatter: (value: number) => string;
  /** The value axis of a stacked bar, shorter than the tooltip: "₹2L". Defaults to `valueFormatter`. */
  axisFormatter?: (value: number) => string;
  /** Height of the chart area in px; the loading skeleton has the same, so nothing shifts. */
  height?: number;
  loading?: boolean;
  /** Shown in the chart area when there is nothing to draw. */
  empty?: ReactNode;
  /** Old data is on screen while new data loads: the chart is dimmed. */
  busy?: boolean;
  /** Load the chart library only when the chart is about to scroll into view. */
  deferUntilVisible?: boolean;
}

const isEmpty = (data: CategoryData): boolean =>
  data.kind === 'donut' ? data.slices.length === 0 : data.categories.length === 0 || data.stacks.length === 0;

/** The legend's entries: a donut's carry the share of the slices shown. */
function entriesOf(data: CategoryData, visible: CategoryData): CategoryLegendEntry[] {
  const total = visible.kind === 'donut' ? sliceTotal(visible.slices) : 0;
  const shown = new Set(legendEntries(visible).map((e) => e.id));
  return legendEntries(data).map((e) => ({
    id: e.id,
    name: e.name,
    color: e.color,
    detail: 'value' in e && shown.has(e.id) ? shareText(e.value, total) : undefined,
  }));
}

/**
 * A donut or a stacked bar for categories (an allocation, a split by account). ECharts draws it on a canvas (lazy chunk,
 * theme colours, no motion when the visitor asks for less); everything the canvas cannot offer is plain HTML next to it:
 * the accessible summary, the legend as toggle buttons, a tooltip, and "Show as table" with the same figures.
 */
export function CategoryChart({
  label,
  data,
  valueFormatter,
  axisFormatter = valueFormatter,
  height = 300,
  loading = false,
  empty,
  busy = false,
  deferUntilVisible = false,
}: CategoryChartProps) {
  const theme = useTheme();
  const tokens = useMemo(() => chartTokens(theme), [theme]);
  const reducedMotion = useMediaQuery('(prefers-reduced-motion: reduce)');
  const phone = useMediaQuery(theme.breakpoints.down('sm'));
  const root = useRef<HTMLDivElement>(null);
  const near = useInView(root, { enabled: deferUntilVisible });
  const tableId = useId();

  const [switchedOff, setHidden] = useState<ReadonlySet<string>>(new Set());
  const [tableOpen, setTableOpen] = useState(false);
  const [failed, setFailed] = useState(false);
  const [attempt, setAttempt] = useState(0);
  const [hover, setHover] = useState<CategoryHover | null>(null);

  // New data can leave nothing switched on (the one slice that was on is gone): then everything is shown again.
  const hidden = useMemo(
    () => (legendEntries(data).every((e) => switchedOff.has(e.id)) ? new Set<string>() : switchedOff),
    [data, switchedOff],
  );
  const visible = useMemo(() => visibleData(data, hidden), [data, hidden]);
  const summary = useMemo(
    () => categorySummary({ label, data: visible, valueFormatter }),
    [label, visible, valueFormatter],
  );
  const option = useMemo(
    () => buildCategoryOption({ data: visible, tokens, axisFormatter, showLabels: !phone, animate: !reducedMotion }),
    [visible, tokens, axisFormatter, phone, reducedMotion],
  );
  const entries = useMemo(() => entriesOf(data, visible), [data, visible]);

  const toggle = (id: string) => {
    setHidden((previous) => {
      const next = new Set(previous);
      if (!next.delete(id)) next.add(id);
      return next;
    });
    setHover(null);
  };
  const tryAgain = () => {
    canvas.Component = lazy(loadCanvas);
    setFailed(false);
    setAttempt((n) => n + 1);
  };
  const Canvas = canvas.Component;
  const hasData = !isEmpty(data);
  const showTable = tableOpen || failed;

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
            <Canvas option={option} onHover={setHover} />
            {hover && (
              <CategoryTooltip
                data={visible}
                hover={hover}
                colors={tokens.categories}
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
          <CategoryLegend
            label={data.kind === 'donut' ? 'Slices shown' : 'Series shown'}
            entries={entries}
            hidden={hidden}
            colors={tokens.categories}
            onToggle={toggle}
          />
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
      )}
      {hasData && !loading && showTable && (
        <CategoryTable id={tableId} label={label} data={visible} valueFormatter={valueFormatter} />
      )}
    </Box>
  );
}
