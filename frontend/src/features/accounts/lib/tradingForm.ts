import type { RiskProfile, TradingInput, TradingSettings } from '@/shared/api';
import type { FieldErrors } from './fieldErrors';

export const instrumentOptions = {
  intraday: ['EQ_MIS', 'FUT'],
  swingBuy: ['EQ_CNC', 'FUT'],
  swingSell: ['FUT'],
} as const;

export type InstrumentGroup = keyof typeof instrumentOptions;
export type OverrideKey = 'riskPerTradePct' | 'maxNewEntriesPerDay' | 'maxOpenPositions' | 'dailyLossPct';

/** Editable text/flags of the trading settings; converted to the API shape on save. */
export interface TradingFormState {
  mode: string;
  riskProfileId: string;
  capitalSource: string;
  capital: string;
  intraday: boolean;
  swing: boolean;
  instruments: Record<InstrumentGroup, string[]>;
  overrides: Record<OverrideKey, string>;
}

/** The profile a new Trading setup starts with: the built-in "Standard" one when present, else the first. */
export function defaultProfileId(profiles: readonly RiskProfile[]): string {
  return (profiles.find((p) => p.isSeeded && p.name === 'Standard') ?? profiles[0])?.id ?? '';
}

const text = (value: number | null | undefined): string => (value === null || value === undefined ? '' : String(value));

/** Starting values: the saved settings, or sensible defaults for an account that has none yet. */
export function tradingFormFrom(settings?: TradingSettings, defaultProfileId = ''): TradingFormState {
  const saved = settings && settings.riskProfileId !== null ? settings : undefined;
  return {
    mode: settings?.mode ?? 'paper',
    riskProfileId: settings?.riskProfileId ?? defaultProfileId,
    capitalSource: settings?.capitalSource ?? 'fixed',
    capital: text(settings?.capitalAmount),
    intraday: saved ? saved.styles.includes('intraday') : false,
    swing: saved ? saved.styles.includes('swing') : true,
    instruments: {
      intraday: [...(settings?.instruments.intraday ?? [])],
      swingBuy: [...(settings?.instruments.swingBuy ?? [])],
      swingSell: [...(settings?.instruments.swingSell ?? [])],
    },
    overrides: {
      riskPerTradePct: text(settings?.overrides.riskPerTradePct),
      maxNewEntriesPerDay: text(settings?.overrides.maxNewEntriesPerDay),
      maxOpenPositions: text(settings?.overrides.maxOpenPositions),
      dailyLossPct: text(settings?.overrides.dailyLossPct),
    },
  };
}

/** Number from a text field; undefined for blank, NaN for garbage (so validation can tell them apart). */
export function parseNumber(value: string): number | undefined {
  const trimmed = value.trim();
  return trimmed === '' ? undefined : Number(trimmed);
}

const orNull = (value: string): number | null => parseNumber(value) ?? null;

export function toTradingInput(state: TradingFormState): TradingInput {
  return {
    mode: state.mode,
    riskProfileId: state.riskProfileId || null,
    capitalSource: state.capitalSource,
    capitalAmount: orNull(state.capital),
    styles: [state.intraday && 'intraday', state.swing && 'swing'].filter((s) => s !== false),
    instruments: { ...state.instruments },
    overrides: {
      riskPerTradePct: orNull(state.overrides.riskPerTradePct),
      maxNewEntriesPerDay: orNull(state.overrides.maxNewEntriesPerDay),
      maxOpenPositions: orNull(state.overrides.maxOpenPositions),
      dailyLossPct: orNull(state.overrides.dailyLossPct),
    },
  };
}

const isCount = (n: number) => Number.isInteger(n) && n >= 1 && n <= 100;

/** Same rules as the server, so mistakes show next to the field before anything is sent. */
export function validateTrading(state: TradingFormState): FieldErrors {
  const errors: Record<string, string> = {};
  if (!state.riskProfileId) {
    errors.riskProfileId = 'Choose a risk profile.';
  }
  const capital = parseNumber(state.capital);
  if (state.capitalSource === 'fixed' && !(capital !== undefined && capital > 0)) {
    errors.capitalAmount = 'Capital must be more than 0.';
  } else if (capital !== undefined && !(capital > 0)) {
    errors.capitalAmount = 'Capital must be more than 0.';
  }
  if (!state.intraday && !state.swing) {
    errors.styles = 'Choose at least one style (intraday or swing).';
  }
  if (state.intraday && state.instruments.intraday.length === 0) {
    errors['instruments.intraday'] = 'Choose at least one instrument for intraday.';
  }
  if (state.swing && state.instruments.swingBuy.length === 0) {
    errors['instruments.swingBuy'] = 'Choose at least one instrument for swing buys.';
  }
  const risk = parseNumber(state.overrides.riskPerTradePct);
  if (risk !== undefined && !(risk > 0 && risk <= 2)) {
    errors['overrides.riskPerTradePct'] = 'Risk per trade must be more than 0 and at most 2 %.';
  }
  for (const key of ['maxNewEntriesPerDay', 'maxOpenPositions'] as const) {
    const value = parseNumber(state.overrides[key]);
    if (value !== undefined && !isCount(value)) {
      errors[`overrides.${key}`] = 'Use a whole number from 1 to 100.';
    }
  }
  const loss = parseNumber(state.overrides.dailyLossPct);
  if (loss !== undefined && !(loss > 0 && loss <= 100)) {
    errors['overrides.dailyLossPct'] = 'Use a percentage above 0 and at most 100.';
  }
  return errors;
}

export interface EffectiveLimits {
  riskPerTradePct: number;
  maxNewEntriesPerDay: number;
  maxOpenPositions: number;
  dailyLossPct: number;
  /** Money risked per trade (1R) when the capital is a fixed amount; null otherwise. */
  rupeesPerR: number | null;
}

/** The limits that apply: the account's override where set and valid, else the risk profile's value. */
export function effectiveLimits(state: TradingFormState, profile: RiskProfile | undefined): EffectiveLimits | null {
  if (!profile) {
    return null;
  }
  const pick = (key: OverrideKey, fallback: number): number => {
    const value = parseNumber(state.overrides[key]);
    return value !== undefined && Number.isFinite(value) && value > 0 ? value : fallback;
  };
  const riskPerTradePct = pick('riskPerTradePct', profile.riskPerTradePct);
  const capital = parseNumber(state.capital);
  return {
    riskPerTradePct,
    maxNewEntriesPerDay: pick('maxNewEntriesPerDay', profile.maxNewEntriesPerDay),
    maxOpenPositions: pick('maxOpenPositions', profile.maxOpenPositions),
    dailyLossPct: pick('dailyLossPct', profile.dailyLossPct),
    rupeesPerR: capital !== undefined && capital > 0 ? (capital * riskPerTradePct) / 100 : null,
  };
}
