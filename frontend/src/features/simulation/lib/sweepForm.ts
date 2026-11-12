import { ApiError } from '@/shared/api';
import { isRealDay } from '@/shared/lib/dates';
import type { ReplaySweepBody } from '../api/replayApi';

export const minSweepStarts = 2;
export const maxSweepStarts = 12;

export interface SweepForm {
  /** `dates`: explicit start dates; `quarters`: the first date and every quarter after it, n starts in all. */
  mode: 'dates' | 'quarters';
  /** Dates separated by commas, spaces or lines. */
  dates: string;
  first: string;
  count: string;
  to: string;
  capital: string;
  riskProfileId: string;
  costs: string;
}

export const initialSweepForm: SweepForm = {
  mode: 'quarters',
  dates: '',
  first: '',
  count: '4',
  to: '',
  capital: '1000000',
  riskProfileId: '',
  costs: 'gross',
};

/** `first` and every quarter after it, `count` dates. A day that a month lacks (31 Nov) falls on the month's last day. */
export function quarterStarts(first: string, count: number): string[] {
  if (!isRealDay(first)) return [];
  const [y = 0, m = 1, d = 1] = first.split('-').map(Number);
  return Array.from({ length: count }, (_, i) => {
    const index = m - 1 + 3 * i;
    const year = y + Math.floor(index / 12);
    const month = index % 12;
    const last = new Date(Date.UTC(year, month + 1, 0)).getUTCDate();
    return `${String(year)}-${String(month + 1).padStart(2, '0')}-${String(Math.min(d, last)).padStart(2, '0')}`;
  });
}

/** The start dates the form stands for, in date order, duplicates removed. A word that is not a real day is kept so it can be named. */
export function sweepStarts(f: SweepForm): string[] {
  const raw =
    f.mode === 'quarters'
      ? quarterStarts(f.first, Math.trunc(Number(f.count)))
      : f.dates.split(/[\s,;]+/).filter(Boolean);
  return [...new Set(raw)].sort();
}

/** The reason the sweep cannot be sent yet, else undefined. */
export function sweepProblem(f: SweepForm): string | undefined {
  if (f.mode === 'quarters' && !isRealDay(f.first)) return 'Choose the first start date.';
  const count = Number(f.count);
  if (f.mode === 'quarters' && !(Number.isInteger(count) && count >= minSweepStarts && count <= maxSweepStarts))
    return `Choose ${String(minSweepStarts)} to ${String(maxSweepStarts)} starts.`;
  const starts = sweepStarts(f);
  const bad = starts.find((s) => !isRealDay(s));
  if (bad) return `"${bad}" is not a real day (write yyyy-mm-dd).`;
  if (starts.length < minSweepStarts || starts.length > maxSweepStarts)
    return `A sweep needs ${String(minSweepStarts)} to ${String(maxSweepStarts)} different start dates.`;
  if (f.to !== '' && !isRealDay(f.to)) return 'The end date is not a real day.';
  if (f.to !== '' && starts.some((s) => s > f.to)) return 'Every start date must be before the end date.';
  const capital = Number(f.capital);
  if (!Number.isFinite(capital) || capital <= 0) return 'Capital must be above 0.';
  return undefined;
}

/** The sweep is frozen mode, selection arm, the auto scope and today's active versions; only these are chosen here. */
export function toSweepBody(f: SweepForm): ReplaySweepBody {
  return {
    starts: sweepStarts(f),
    to: f.to === '' ? null : f.to,
    scope: { basketIds: [], instrumentIds: [] },
    capital: Number(f.capital),
    riskProfileId: f.riskProfileId === '' ? null : f.riskProfileId,
    costs: f.costs,
    versions: 'start',
    filtersWithoutHistory: false,
    includeResearchCells: false,
  };
}

/** The API's own sentence for a refusal. 422 replay.invalid names each bad start; 409 replay.busy says a replay is running. */
export function sweepRefusal(error: unknown): string {
  if (!(error instanceof ApiError)) return 'The sweep could not be started.';
  const { problem } = error;
  const detail = problem?.detail ?? problem?.title ?? error.message;
  if (problem?.code === 'replay.busy') return `${detail} Wait for it to finish or cancel it, then start the sweep.`;
  if (problem?.code === 'replay.invalid') return `The sweep was not started. ${detail}`;
  return detail;
}
