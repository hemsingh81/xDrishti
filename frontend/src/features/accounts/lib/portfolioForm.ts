import type { AccountDetail, PortfolioInput } from '@/shared/api';
import { validateAcquiredOn } from '@/shared/lib/dates';
import type { FieldErrors } from './fieldErrors';
import { autoRefreshFrom, toRefreshInput, validateAutoRefresh, type AutoRefreshState } from './autoRefresh';

export interface PortfolioFormState {
  includeInTotals: boolean;
  /** How the holdings refresh by themselves. */
  refresh: AutoRefreshState;
  /** yyyy-MM-dd: the date the first import gives holdings that have no date of their own. */
  firstImportDate: string;
}

/** Until the owner chooses another, the first import dates holdings 1 Jan 2025 (the server's default too). */
export const defaultFirstImportDate = '2025-01-01';

/** The settings as the form shows them. */
export function portfolioFormFrom(settings?: AccountDetail['portfolio']): PortfolioFormState {
  return {
    includeInTotals: settings?.includeInTotals ?? true,
    refresh: autoRefreshFrom(settings),
    firstImportDate: settings?.firstImportDate ?? defaultFirstImportDate,
  };
}

/**
 * The request for the account's Portfolio tab and the wizard. There is no bucket choice in the UI (ADR 0015), so
 * `defaultBucket` is left out: the API then keeps the account's own bucket (a custom one set earlier stays too).
 * The generated type lists the key as required (nullable), hence the one cast.
 */
export function toPortfolioInput(state: PortfolioFormState): PortfolioInput {
  const body: Omit<PortfolioInput, 'defaultBucket'> = {
    includeInTotals: state.includeInTotals,
    ...toRefreshInput(state.refresh),
    firstImportDate: state.firstImportDate,
  };
  return body as PortfolioInput;
}

/** Client-side checks of the portfolio settings, keyed like the API's fields; empty when valid. */
export function validatePortfolio(state: PortfolioFormState): FieldErrors {
  const errors: Record<string, string> = { ...validateAutoRefresh(state.refresh) };
  const date = validateAcquiredOn(state.firstImportDate);
  if (date) errors.firstImportDate = date;
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
