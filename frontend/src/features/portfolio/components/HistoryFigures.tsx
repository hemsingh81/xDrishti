import { Box, Tooltip } from '@mui/material';
import type { UseQueryResult } from '@tanstack/react-query';
import { useState, type ReactNode } from 'react';
import { formatDate } from '@/shared/lib/format';
import type { HistoryBenchmark, HoldingsSummary, PortfolioHistory } from '@/shared/api';
import { alphaOf, benchmarkName, benchmarkXirrOf, pointsDirection, signedPoints } from '../lib/benchmark';
import { drawdownDetail, drawdownDirection, drawdownText } from '../lib/drawdown';
import { directionOf, signedPercent } from '../lib/figures';
import { Trend, Unavailable } from './Figures';

/**
 * A figure with a longer explanation: it is the element's accessible name together with the figure, a tooltip, and a tab
 * stop so the tooltip opens from the keyboard too.
 */
function Described({ figure, detail, children }: { figure: string; detail: string; children: ReactNode }) {
  const [open, setOpen] = useState(false);
  return (
    <Tooltip
      title={detail}
      open={open}
      onOpen={() => {
        setOpen(true);
      }}
      onClose={() => {
        setOpen(false);
      }}
    >
      <Box
        component="span"
        role="img"
        aria-label={`${figure}. ${detail}`}
        tabIndex={0}
        onFocus={() => {
          setOpen(true);
        }}
        onBlur={() => {
          setOpen(false);
        }}
      >
        {children}
      </Box>
    </Tooltip>
  );
}

interface BenchmarkProps {
  summary: HoldingsSummary;
  /** What the history says about the benchmark: its name and, when it has no figures, why. */
  benchmark: HistoryBenchmark | undefined;
}

/** The XIRR of the same cash flows put into the benchmark index; "—" with the reason when there is none. */
export function BenchmarkXirr({ summary, benchmark }: BenchmarkProps) {
  const figure = benchmarkXirrOf(summary, { name: benchmarkName(benchmark), reason: benchmark?.reason });
  if ('value' in figure) return <Trend direction={directionOf(figure.value, 2)}>{signedPercent(figure.value)}</Trend>;
  return <Unavailable label={figure.label} reason={figure.reason} focusable />;
}

/** The portfolio's XIRR minus the benchmark's, in percentage points, with sign and arrow; "—" with the reason when there is none. */
export function Alpha({ summary, benchmark }: BenchmarkProps) {
  const figure = alphaOf(summary, { name: benchmarkName(benchmark), reason: benchmark?.reason });
  if (!('value' in figure)) return <Unavailable label={figure.label} reason={figure.reason} focusable />;
  const text = signedPoints(figure.value);
  const value = <Trend direction={pointsDirection(figure.value)}>{text}</Trend>;
  if (summary.alphaBasis !== 'live_vs_last_close' || !summary.benchmarkAsOf) return value;
  // The index has no live quote: the portfolio is at today's prices, the index at its last close.
  return (
    <Described
      figure={text}
      detail={`Portfolio at live prices against ${benchmarkName(benchmark)} at its last close (${formatDate(summary.benchmarkAsOf)}): the alpha includes the day's move of the portfolio only.`}
    >
      {value}
    </Described>
  );
}

/** The largest fall of the history in the chosen range, with its dates in a tooltip. */
export function MaxDrawdown({ query, rangeLabel }: { query: UseQueryResult<PortfolioHistory>; rangeLabel: string }) {
  // While the range or scope changes the old figure would sit under the new label: wait for the new one.
  if (query.isPending || query.isPlaceholderData)
    return (
      <Box component="span" aria-busy>
        …
      </Box>
    );
  if (query.isError)
    return <Unavailable label="—" reason="The value history could not be loaded, so there is no drawdown." focusable />;
  const { maxDrawdown, benchmarkMaxDrawdown, benchmark, rangeFrom, rangeTo } = query.data;
  if (!maxDrawdown) {
    return (
      <Unavailable
        label="—"
        reason="There is no value history in this range yet, so there is no drawdown. Track your holdings and run Update data."
        focusable
      />
    );
  }
  const text = drawdownText(maxDrawdown);
  const detail = drawdownDetail(maxDrawdown, benchmarkMaxDrawdown, {
    rangeLabel,
    range: { from: rangeFrom, to: rangeTo },
    benchmarkName: benchmarkName(benchmark),
  });
  return (
    <Described figure={text} detail={detail}>
      <Trend direction={drawdownDirection(maxDrawdown)}>{text}</Trend>
    </Described>
  );
}
