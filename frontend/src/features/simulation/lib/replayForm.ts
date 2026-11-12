import { ApiError } from '@/shared/api';
import { isRealDay } from '@/shared/lib/dates';
import type { ReplayStartBody } from '../api/replayApi';

export type ReplayModes = 'frozen' | 'learning' | 'both';

export interface ReplayForm {
  from: string;
  to: string;
  scope: 'auto' | 'baskets';
  basketIds: string[];
  capital: string;
  riskProfileId: string;
  /** `gross`, or the id of a stored charge sheet. */
  costs: string;
  /** Empty = the server's default. */
  versions: '' | 'start' | 'asOf';
  modes: ReplayModes;
  filtersWithoutHistory: boolean;
  includeResearchCells: boolean;
}

export const initialForm: ReplayForm = {
  from: '',
  to: '',
  scope: 'auto',
  basketIds: [],
  capital: '1000000',
  riskProfileId: '',
  costs: 'gross',
  versions: '',
  modes: 'frozen',
  filtersWithoutHistory: false,
  includeResearchCells: false,
};

/** The reason the form cannot be sent yet, else undefined. */
export function formProblem(f: ReplayForm): string | undefined {
  if (!isRealDay(f.from)) return 'Choose the start date.';
  if (f.to !== '' && !isRealDay(f.to)) return 'The end date is not a real day.';
  if (f.to !== '' && f.from > f.to) return 'The start date is after the end date.';
  if (f.scope === 'baskets' && f.basketIds.length === 0) return 'Choose at least one basket.';
  const capital = Number(f.capital);
  if (!Number.isFinite(capital) || capital <= 0) return 'Capital must be above 0.';
  return undefined;
}

const modeList: Record<ReplayModes, string[]> = {
  frozen: ['frozen'],
  learning: ['learning'],
  both: ['frozen', 'learning'],
};

export function toBody(f: ReplayForm): ReplayStartBody {
  return {
    from: f.from,
    to: f.to === '' ? null : f.to,
    scope: { basketIds: f.scope === 'baskets' ? f.basketIds : [], instrumentIds: [] },
    capital: Number(f.capital),
    riskProfileId: f.riskProfileId === '' ? null : f.riskProfileId,
    costs: f.costs,
    versions: f.versions === '' ? null : f.versions,
    modes: modeList[f.modes],
    filtersWithoutHistory: f.filtersWithoutHistory,
    includeResearchCells: f.includeResearchCells,
  };
}

/** The API's own sentence for a refusal (replay.invalid, replay.busy, replay.holdout), else a plain fallback. */
export function refusalText(error: unknown): string {
  if (error instanceof ApiError) return error.problem?.detail ?? error.problem?.title ?? error.message;
  return 'The replay could not be started.';
}
