import type { AccountDetail, PortfolioBucket, PortfolioInput } from '@/shared/api';
import { validateAcquiredOn } from '@/shared/lib/dates';
import type { FieldErrors } from './fieldErrors';
import { autoRefreshFrom, toRefreshInput, validateAutoRefresh, type AutoRefreshState } from './autoRefresh';

export interface PortfolioFormState {
  /** Name of a custom bucket, or blank for the account's own default bucket. */
  defaultBucket: string;
  includeInTotals: boolean;
  /** How the holdings refresh by themselves. */
  refresh: AutoRefreshState;
  /** yyyy-MM-dd: the date the first import gives holdings that have no date of their own. */
  firstImportDate: string;
}

/** Until the owner chooses another, the first import dates holdings 1 Jan 2025 (the server's default too). */
export const defaultFirstImportDate = '2025-01-01';

/** The name of an account's own default bucket, as the server builds it ("{account name} · Portfolio"). */
export const autoBucketNameOf = (accountName: string): string => `${accountName.trim() || 'Account name'} · Portfolio`;

/** The settings as the form shows them; the account's own bucket is stored as "blank" so renames follow along. */
export function portfolioFormFrom(settings?: AccountDetail['portfolio']): PortfolioFormState {
  const own = settings?.defaultBucket === settings?.autoBucketName;
  return {
    defaultBucket: settings && !own ? settings.defaultBucket : '',
    includeInTotals: settings?.includeInTotals ?? true,
    refresh: autoRefreshFrom(settings),
    firstImportDate: settings?.firstImportDate ?? defaultFirstImportDate,
  };
}

/**
 * The request for the account's Portfolio tab. The API keeps whatever is null or left out, so the account's own bucket
 * is sent as an empty string (that is a choice), a custom bucket by name.
 */
export function toPortfolioInput(state: PortfolioFormState): PortfolioInput {
  return {
    defaultBucket: state.defaultBucket,
    includeInTotals: state.includeInTotals,
    ...toRefreshInput(state.refresh),
    firstImportDate: state.firstImportDate,
  };
}

/** Client-side checks of the portfolio settings, keyed like the API's fields; empty when valid. */
export function validatePortfolio(state: PortfolioFormState): FieldErrors {
  const errors: Record<string, string> = { ...validateAutoRefresh(state.refresh) };
  const date = validateAcquiredOn(state.firstImportDate);
  if (date) errors.firstImportDate = date;
  return errors;
}

/**
 * The buckets that can be picked besides the account's own: custom ones only. Default buckets of accounts (this
 * account's own is a separate option, other accounts' are never offered).
 */
export const customBuckets = (buckets: readonly PortfolioBucket[]): PortfolioBucket[] =>
  buckets.filter((b) => !b.isAccountDefault);

export const maxLiveFeedBudget = 5000;

/** Live-feed budget text → error message (undefined when valid). */
export function validateLiveFeedBudget(value: string): string | undefined {
  const n = value.trim() === '' ? Number.NaN : Number(value);
  return Number.isInteger(n) && n >= 0 && n <= maxLiveFeedBudget
    ? undefined
    : `Use a whole number from 0 to ${maxLiveFeedBudget.toLocaleString('en-IN')}.`;
}
