import { formatCount, formatR, formatRate, metricOf, type EvidenceProps, type MetricState } from '@/shared/analytics';
import { formatDate } from '@/shared/lib/format';
import type { CellStats } from '../api/learningQueries';

/** Presentation constants of the heatmap. Tints are alpha over the card; `heat.test.ts` holds their contrast. */
export const heat = { hatchAlpha: 0.15, tintMin: 0.08, tintMax: 0.28, fullAtR: 0.5 } as const;

const unitMinutes: Readonly<Record<string, number>> = { m: 1, h: 60, d: 1440, w: 10080 };

/** Minutes in a timeframe code ("15m" 15, "1h" 60, "1D" 1440); an unknown code sorts last. */
export function timeframeMinutes(code: string): number {
  const match = /^(\d+)\s*([a-zA-Z]+)$/.exec(code);
  const unit = unitMinutes[(match?.[2] ?? '').toLowerCase().charAt(0)];
  return match?.[1] !== undefined && unit !== undefined ? Number(match[1]) * unit : Number.POSITIVE_INFINITY;
}

export interface GridRow {
  id: string;
  strategy: string;
  /** side, style, exit and (when it is not "all") regime. */
  detail: string;
  /** The stored cell of this row in each timeframe, by timeframe code. */
  cells: ReadonlyMap<string, CellStats>;
}

export interface Grid {
  timeframes: string[];
  rows: GridRow[];
}

/**
 * Strategy rows by timeframe columns. A (strategy, timeframe) pair holds several cells (side, style, exit, regime), and the
 * browser computes no statistic, so those are not merged: the other parts of the key make the row, and every tile is exactly
 * one stored cell.
 */
export function buildGrid(cells: readonly CellStats[]): Grid {
  const rows = new Map<string, GridRow & { cells: Map<string, CellStats> }>();
  const timeframes = new Set<string>();
  for (const cell of cells) {
    timeframes.add(cell.timeframe);
    const id = [cell.strategy, cell.side, cell.style, cell.exit, cell.regime].join('|');
    const row = rows.get(id) ?? {
      id,
      strategy: cell.strategy,
      detail: [cell.side, cell.style, cell.exit, cell.regime === '*' ? null : cell.regime]
        .filter((part) => part !== null)
        .join(' · '),
      cells: new Map<string, CellStats>(),
    };
    row.cells.set(cell.timeframe, cell);
    rows.set(id, row);
  }
  return {
    timeframes: [...timeframes].sort((a, b) => timeframeMinutes(a) - timeframeMinutes(b) || a.localeCompare(b)),
    rows: [...rows.values()].sort((a, b) => a.strategy.localeCompare(b.strategy) || a.detail.localeCompare(b.detail)),
  };
}

/**
 * A derived statistic of a cell. Below the server's minimum (`enough` false) it is `insufficient` and carries no number, so a
 * thin cell cannot read as a winner; a statistic the server did not store is `unavailable` with the reason.
 */
export function statState(cell: CellStats, value: number | null | undefined, missing: string): MetricState {
  if (!cell.enough) return { kind: 'insufficient', n: cell.n, min: cell.minTrades };
  return metricOf(value, missing);
}

export const expectancyOf = (cell: CellStats) =>
  statState(cell, cell.expectancyR, 'No expectancy is stored for this cell');
export const lowerBoundOf = (cell: CellStats) =>
  statState(cell, cell.winRateLower, 'No lower bound is stored for this cell');

/** Tint strength for an expectancy: stronger with the size of the printed figure, never zero. */
export function tintAlpha(expectancyR: number): number {
  const share = Math.min(1, Math.abs(expectancyR) / heat.fullAtR);
  return heat.tintMin + (heat.tintMax - heat.tintMin) * share;
}

const basisKind = (basis: string): 'net' | 'gross' | null =>
  /^net\b/i.test(basis) ? 'net' : /^gross\b/i.test(basis) ? 'gross' : null;

/** What the rows say about costs: one known basis for all, else not stated. */
export function basisOf(cells: readonly CellStats[]): EvidenceProps['basis'] {
  const kinds = new Set(cells.map((cell) => basisKind(cell.basis)));
  const only = kinds.size === 1 ? [...kinds][0] : null;
  return only ?? 'unknown';
}

/**
 * The evidence for a set of cell rows: trades summed over the cells (one run per strategy feeds a snapshot, so a trade is in
 * one cell per window), the snapshot and its date as "Data used", the server's limits text. The rows carry no data range, so
 * it is not stated.
 */
export function cellsEvidence(cells: readonly CellStats[]): Omit<EvidenceProps, 'compact' | 'label'> {
  const snapshots = new Map<string, string>();
  for (const cell of cells) snapshots.set(cell.snapshotKey, cell.asOf);
  const basis = basisOf(cells);
  const limits = [...new Set(cells.map((cell) => cell.limits))];
  return {
    n: cells.reduce((total, cell) => total + cell.n, 0),
    range: null,
    basis,
    population: [
      `${cells.length} ${cells.length === 1 ? 'cell' : 'cells'}`,
      ...[...snapshots].map(([key, asOf]) => `snapshot ${key}, as of ${formatDate(asOf)}`),
    ],
    limits:
      basis === 'unknown' && cells.length > 0
        ? [...limits, 'The rows mix gross and net R, or do not state it.']
        : limits,
  };
}

/** The accessible name: the whole cell and its state in words, so the tile never depends on colour or hatching. */
export function tileLabel(cell: CellStats): string {
  const head = `${cell.strategy}, ${cell.timeframe}, ${cell.side}, ${cell.style}, ${cell.exit}`;
  if (!cell.enough) {
    return `${head}: insufficient data, n ${formatCount(cell.n)} of at least ${formatCount(cell.minTrades)}, not enough evidence`;
  }
  const expectancy = cell.expectancyR === null ? 'expectancy not available' : `expectancy ${formatR(cell.expectancyR)}`;
  return `${head}: ${expectancy}, n ${formatCount(cell.n)}, win-rate lower bound ${formatRate(cell.winRateLower)}, enough evidence`;
}
