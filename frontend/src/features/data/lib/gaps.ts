import type { DhanPhase, GapsOverview } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';

/** The states of a gap (ADR 0017 §3); the Gaps list filters by one of them or shows all. */
export const gapStatuses = ['open', 'unfillable', 'filled'] as const;
export type GapStatus = (typeof gapStatuses)[number];

export const gapsViews = [...gapStatuses, 'all'] as const;
export type GapsView = (typeof gapsViews)[number];

export const gapsViewLabels: Readonly<Record<GapsView, string>> = {
  open: 'Open',
  unfillable: 'Unfillable',
  filled: 'Filled',
  all: 'All',
};

export const isGapsView = (value: string | null): value is GapsView => gapsViews.some((v) => v === value);

export const isGapStatus = (value: string): value is GapStatus => gapStatuses.some((s) => s === value);

/** "12 open · 3 unfillable"; "No open gaps" when there are none (unfillable ones are still named). */
export function gapsSummary({ open, unfillable }: Pick<GapsOverview, 'open' | 'unfillable'>): string {
  const parts = [
    open > 0 ? `${formatNumber(open)} open` : null,
    unfillable > 0 ? `${formatNumber(unfillable)} unfillable` : null,
  ].filter((part): part is string => part !== null);
  return parts.length > 0 ? parts.join(' · ') : 'No open gaps';
}

/** Gaps still open after the run (found, minus filled, minus the ones a refetch could not fill). */
export const openAfterRun = (phase: Pick<DhanPhase, 'gapsFound' | 'gapsFilled' | 'gapsUnfillable'>): number =>
  Math.max(0, phase.gapsFound - phase.gapsFilled - phase.gapsUnfillable);

/** "5 found · 3 filled · 1 unfillable · 1 still open" for a run that found gaps; null when it found none. */
export function runGapsText(phase: Pick<DhanPhase, 'gapsFound' | 'gapsFilled' | 'gapsUnfillable'>): string | null {
  if (phase.gapsFound <= 0) return null;
  const open = openAfterRun(phase);
  return [
    `${formatNumber(phase.gapsFound)} found`,
    `${formatNumber(phase.gapsFilled)} filled`,
    `${formatNumber(phase.gapsUnfillable)} unfillable`,
    open > 0 ? `${formatNumber(open)} still open` : null,
  ]
    .filter((part): part is string => part !== null)
    .join(' · ');
}
