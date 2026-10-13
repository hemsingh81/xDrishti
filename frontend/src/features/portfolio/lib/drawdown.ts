import type { Drawdown, PortfolioHistory } from '@/shared/api';
import { formatDate, formatDayMonth, formatPercent } from '@/shared/lib/format';
import { directionOf, type Direction } from './figures';

/** "−12.50%": the fall as the signed number it is; "0.00%" when the series never fell. */
export function drawdownText(drawdown: Drawdown): string {
  return directionOf(drawdown.pct, 2) === 'flat' ? '0.00%' : `−${formatPercent(drawdown.pct)}%`;
}

export const drawdownDirection = (drawdown: Drawdown): Direction =>
  directionOf(drawdown.pct, 2) === 'flat' ? 'flat' : 'down';

/** Under the figure: "3 Mar → 9 Apr" (peak to low), or that there was no fall in the range. */
export function drawdownHint(drawdown: Drawdown): string {
  if (drawdownDirection(drawdown) === 'flat' || !drawdown.from || !drawdown.to) return 'No fall in this range';
  return `${formatDayMonth(drawdown.from)} → ${formatDayMonth(drawdown.to)}`;
}

/** The detail behind the figure, for the tooltip and the accessible name: the dates and the benchmark's fall. */
export function drawdownDetail(
  drawdown: Drawdown,
  benchmark: Drawdown | null,
  {
    rangeLabel,
    range,
    benchmarkName,
  }: { rangeLabel: string; range?: { from: string | null; to: string | null }; benchmarkName: string },
): string {
  // "1Y: 13 Oct 2025 – 9 Oct 2026": the range as the server returned it, not only the chip.
  const span =
    range?.from && range.to ? `${rangeLabel}: ${formatDate(range.from)} – ${formatDate(range.to)}` : rangeLabel;
  const own =
    drawdownDirection(drawdown) === 'flat' || !drawdown.from || !drawdown.to
      ? `The portfolio's unit value did not fall in this range (${span}).`
      : `The portfolio's unit value fell ${formatPercent(drawdown.pct)}% from its peak on ${formatDate(drawdown.from)} to its low on ${formatDate(drawdown.to)} (range ${span}; cash flows do not count as gains or losses).`;
  const index = benchmark ? ` ${benchmarkName} fell ${formatPercent(benchmark.pct)}% over the same range.` : '';
  return own + index;
}

/** Under the max drawdown tile: the dates of the fall, or why there is none yet. */
export function drawdownTileHint(history: {
  isPending: boolean;
  isPlaceholderData: boolean;
  isError: boolean;
  data: PortfolioHistory | undefined;
}): string {
  if (history.isPending || history.isPlaceholderData) return 'Loading';
  if (history.isError || !history.data?.maxDrawdown) return 'No history yet';
  return drawdownHint(history.data.maxDrawdown);
}
