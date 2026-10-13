import type { PortfolioInput } from '@/shared/api';
import type { FieldErrors } from './fieldErrors';

export const refreshModes = ['off', 'daily'] as const;
export type RefreshMode = (typeof refreshModes)[number];

/** How an account's holdings refresh by themselves; the time is kept while the mode is off. */
export interface AutoRefreshState {
  mode: RefreshMode;
  /** "HH:mm", India time. */
  dailyTime: string;
}

/** What a new account starts with: off (the owner turns it on). */
export const defaultAutoRefresh: AutoRefreshState = { mode: 'off', dailyTime: '16:00' };

const isMode = (value: string): value is RefreshMode => refreshModes.some((m) => m === value);

interface RefreshSettings {
  refreshMode: string;
  refreshDailyTimeIst: string;
}

/** The settings as the form shows them; anything unknown from an older or newer API reads as "off". */
export function autoRefreshFrom(settings?: RefreshSettings): AutoRefreshState {
  if (!settings) return defaultAutoRefresh;
  return {
    mode: isMode(settings.refreshMode) ? settings.refreshMode : 'off',
    dailyTime: settings.refreshDailyTimeIst,
  };
}

const timePattern = /^([01]\d|2[0-3]):[0-5]\d$/;

/** Field messages keyed like the API's (`refreshDailyTimeIst`); empty when valid. */
export function validateAutoRefresh(state: AutoRefreshState): FieldErrors {
  const errors: Record<string, string> = {};
  if (state.mode === 'daily' && !timePattern.test(state.dailyTime)) {
    errors.refreshDailyTimeIst = 'Enter a time as HH:MM, for example 16:00.';
  }
  return errors;
}

/** The request fields. A time that is not usable and that the mode ignores is left out (the server keeps its own). */
export function toRefreshInput(state: AutoRefreshState): Pick<PortfolioInput, 'refreshMode' | 'refreshDailyTimeIst'> {
  return {
    refreshMode: state.mode,
    refreshDailyTimeIst: timePattern.test(state.dailyTime) ? state.dailyTime : undefined,
  };
}

/** "Off" or "Daily at 16:00 IST": the setting in words. */
export const refreshSummary = (state: AutoRefreshState): string =>
  state.mode === 'daily' ? `Daily at ${state.dailyTime} IST` : 'Off';
