import { Alert, Box, Button, Stack, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { visuallyHidden } from '@mui/utils';
import { lazy, Suspense, useId, useMemo, useState, type ReactNode } from 'react';
import { ChartBoundary } from './ChartBoundary';
import { ChartSkeleton } from './ChartSkeleton';
import { PriceBarsTable } from './PriceBarsTable';
import { buildMarkers, drawableLines, priceSummary, toCandles } from './priceChartModel';
import { priceChartColors } from './priceChartTheme';
import type { PriceBar, PriceEvent, PriceLine } from './priceChartTypes';

// The chart library is a separate lazy chunk. A failed fetch is remembered by `lazy`, so "Try again" swaps in a fresh one.
const loadCanvas = () => import('./PriceChartCanvas');
const canvas = { Component: lazy(loadCanvas) };

export interface PriceChartProps {
  /** What the chart shows ("ORB BUY RELIANCE, 5m"): its accessible name. */
  label: string;
  bars: readonly PriceBar[];
  lines?: readonly PriceLine[];
  events?: readonly PriceEvent[];
  /** The day timeframe labels its axis with days. */
  dayOnly?: boolean;
  height?: number;
  /** Shown in the chart area when there are no bars. */
  empty?: ReactNode;
}

/**
 * A candlestick chart of a stretch of price (TradingView Lightweight Charts in a lazy chunk, hidden behind this props type so a
 * swap touches one file). The picture is never the only carrier of information: "Previous event" / "Next event" move the
 * crosshair and are announced, and "Bars (n)" opens the same bars as a table.
 */
export function PriceChart({
  label,
  bars,
  lines = [],
  events = [],
  dayOnly = false,
  height = 360,
  empty,
}: PriceChartProps) {
  const theme = useTheme();
  const colors = useMemo(() => priceChartColors(theme), [theme]);
  const candles = useMemo(() => toCandles(bars), [bars]);
  const levels = useMemo(() => drawableLines(lines), [lines]);
  const placed = useMemo(() => buildMarkers(events, bars), [events, bars]);
  const summary = useMemo(() => priceSummary(label, bars, levels, events), [label, bars, levels, events]);
  const tableId = useId();
  const [tableOpen, setTableOpen] = useState(false);
  const [at, setAt] = useState(-1);
  const [failed, setFailed] = useState(false);
  const [attempt, setAttempt] = useState(0);

  const ordered = useMemo(() => [...events].sort((a, b) => a.t - b.t), [events]);
  const current = at >= 0 ? ordered[at] : undefined;
  const focus = current
    ? { time: placed.markers.find((m) => m.id === current.id)?.time ?? current.t, price: current.price }
    : null;
  const step = (delta: number) => {
    setAt((previous) =>
      Math.min(ordered.length - 1, Math.max(0, (previous < 0 ? (delta > 0 ? -1 : ordered.length) : previous) + delta)),
    );
  };
  const Canvas = canvas.Component;

  if (bars.length === 0) return <>{empty}</>;
  return (
    <Box>
      <ChartBoundary
        key={attempt}
        fallback={
          <Alert
            severity="warning"
            action={
              <Button
                color="inherit"
                size="small"
                onClick={() => {
                  canvas.Component = lazy(loadCanvas);
                  setFailed(false);
                  setAttempt((n) => n + 1);
                }}
              >
                Try again
              </Button>
            }
          >
            The chart could not be loaded. The tables have the same figures.
          </Alert>
        }
        onError={() => {
          setFailed(true);
        }}
      >
        <Suspense fallback={<ChartSkeleton height={height} />}>
          <Box role="img" aria-label={summary} sx={{ height, position: 'relative' }}>
            <Canvas
              candles={candles}
              lines={levels}
              markers={placed.markers}
              colors={colors}
              height={height}
              dayOnly={dayOnly}
              focus={focus}
            />
          </Box>
        </Suspense>
      </ChartBoundary>
      {placed.outside.length > 0 && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>
          {`Outside this window, not drawn: ${placed.outside.map((e) => e.announce).join('; ')}.`}
        </Typography>
      )}
      <Stack
        direction="row"
        sx={{ mt: 1, flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', gap: 1 }}
      >
        <Stack direction="row" sx={{ gap: 0.5 }}>
          <Button
            size="small"
            disabled={ordered.length === 0 || failed}
            onClick={() => {
              step(-1);
            }}
          >
            Previous event
          </Button>
          <Button
            size="small"
            disabled={ordered.length === 0 || failed}
            onClick={() => {
              step(1);
            }}
          >
            Next event
          </Button>
        </Stack>
        <Button
          size="small"
          aria-expanded={tableOpen || failed}
          aria-controls={tableId}
          disabled={failed}
          onClick={() => {
            setTableOpen((open) => !open);
          }}
        >
          {`Bars (${String(bars.length)})`}
        </Button>
      </Stack>
      <Box role="status" aria-live="polite" sx={visuallyHidden}>
        {current?.announce}
      </Box>
      {(tableOpen || failed) && <PriceBarsTable id={tableId} label={label} bars={bars} />}
    </Box>
  );
}
