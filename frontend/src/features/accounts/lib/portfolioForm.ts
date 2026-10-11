import type { AccountDetail, PortfolioInput } from '@/shared/api';
import { istDayIso } from '@/shared/lib/format';
import type { FieldErrors } from './fieldErrors';

export interface PortfolioFormState {
  defaultBucket: string;
  /** yyyy-MM-dd or blank. */
  historyFrom: string;
  includeInTotals: boolean;
}

export const earliestHistoryDate = '2000-01-01';

/** Today as yyyy-MM-dd on the Indian calendar (IST), which is what the server checks against. */
export function todayIso(now: Date = new Date()): string {
  return istDayIso(now);
}

export function portfolioFormFrom(settings?: AccountDetail['portfolio'], defaultBucket = ''): PortfolioFormState {
  return {
    defaultBucket: settings?.defaultBucket ?? defaultBucket,
    historyFrom: settings?.historyFrom ?? '',
    includeInTotals: settings?.includeInTotals ?? true,
  };
}

export function toPortfolioInput(state: PortfolioFormState): PortfolioInput {
  return {
    defaultBucket: state.defaultBucket || null,
    historyFrom: state.historyFrom || null,
    includeInTotals: state.includeInTotals,
  };
}

export function validatePortfolio(state: PortfolioFormState, today: string = todayIso()): FieldErrors {
  const errors: Record<string, string> = {};
  if (!state.defaultBucket) {
    errors.defaultBucket = 'Choose a default bucket.';
  }
  if (state.historyFrom && (state.historyFrom < earliestHistoryDate || state.historyFrom > today)) {
    errors.historyFrom = `Pick a date from ${earliestHistoryDate} up to today.`;
  }
  return errors;
}

export const maxLiveFeedBudget = 5000;

/** Live-feed budget text → error message (undefined when valid). */
export function validateLiveFeedBudget(value: string): string | undefined {
  const n = value.trim() === '' ? Number.NaN : Number(value);
  return Number.isInteger(n) && n >= 0 && n <= maxLiveFeedBudget
    ? undefined
    : `Use a whole number from 0 to ${maxLiveFeedBudget.toLocaleString('en-IN')}.`;
}
