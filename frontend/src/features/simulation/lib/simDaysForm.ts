import { isRealDay } from '@/shared/lib/dates';
import type { SimDaysStartBody } from '../api/simDaysApi';

export interface SimDaysForm {
  start: string;
  /** Empty: the last complete stored session. */
  end: string;
  basketIds: string[];
  /** The chosen stocks, id to symbol. */
  stocks: { id: string; symbol: string }[];
  capital: string;
  mode: 'learning' | 'frozen';
}

/** The default capital of `Sim:DefaultCapital` (₹10,00,000); the API applies it too when the field is empty. */
export const defaultCapital = '1000000';

export const initialSimDaysForm: SimDaysForm = {
  start: '',
  end: '',
  basketIds: [],
  stocks: [],
  capital: defaultCapital,
  mode: 'learning',
};

/** The reason the form cannot be sent yet, else undefined. */
export function simDaysProblem(f: SimDaysForm): string | undefined {
  if (!isRealDay(f.start)) return 'Choose the start date.';
  if (f.end !== '' && !isRealDay(f.end)) return 'The end date is not a real day.';
  if (f.end !== '' && f.start > f.end) return 'The start date is after the end date.';
  if (f.basketIds.length === 0 && f.stocks.length === 0) return 'Choose at least one stock or basket.';
  const capital = Number(f.capital);
  if (f.capital.trim() === '' || !Number.isFinite(capital) || capital <= 0) return 'Capital must be above 0.';
  return undefined;
}

export function toSimDaysBody(f: SimDaysForm): SimDaysStartBody {
  return {
    start: f.start,
    end: f.end === '' ? null : f.end,
    scope: { basketIds: f.basketIds, instrumentIds: f.stocks.map((s) => s.id) },
    capital: Number(f.capital),
    riskProfileId: null,
    mode: f.mode,
  };
}

const waitingText: Record<string, string> = {
  market_hours: 'market hours',
  low_disk: 'low disk space',
  data_job: 'a data job is running',
  backfill: 'the backfill is not complete',
  sim_all: 'a simulate-everything chunk is running',
};

/** Why the runner waits, in words ("waiting: market hours"). */
export const waitingLabel = (waiting: string) => `waiting: ${waitingText[waiting] ?? waiting.replace(/_/g, ' ')}`;
