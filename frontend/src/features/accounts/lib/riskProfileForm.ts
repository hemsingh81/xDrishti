import type { RiskProfile, RiskProfileInput } from '@/shared/api';
import type { FieldErrors } from './fieldErrors';
import { parseNumber } from './tradingForm';

/** How a limit is validated: more than 0 up to `max`, a whole count 1–100, or a ratio 0–1. */
type Kind = { type: 'upTo'; max: number } | { type: 'count' } | { type: 'ratio' };

export const riskFields = [
  { key: 'riskPerTradePct', label: 'Risk per trade (%)', unit: '%', step: 'any', kind: { type: 'upTo', max: 2 } },
  {
    key: 'maxCapitalPerTradePct',
    label: 'Max capital per trade (%)',
    unit: '%',
    step: 'any',
    kind: { type: 'upTo', max: 100 },
  },
  { key: 'maxNewEntriesPerDay', label: 'Max new entries per day', unit: '', step: '1', kind: { type: 'count' } },
  { key: 'maxOpenPositions', label: 'Max open positions', unit: '', step: '1', kind: { type: 'count' } },
  {
    key: 'maxTotalRiskPct',
    label: 'Max total open risk (%)',
    unit: '%',
    step: 'any',
    kind: { type: 'upTo', max: 100 },
  },
  { key: 'maxPerSector', label: 'Max per sector', unit: '', step: '1', kind: { type: 'count' } },
  { key: 'maxPairCorrelation', label: 'Max pair correlation (0–1)', unit: '', step: 'any', kind: { type: 'ratio' } },
  { key: 'maxIntraday', label: 'Max intraday positions', unit: '', step: '1', kind: { type: 'count' } },
  { key: 'maxSwing', label: 'Max swing positions', unit: '', step: '1', kind: { type: 'count' } },
  { key: 'dailyLossR', label: 'Daily loss limit (R)', unit: 'R', step: 'any', kind: { type: 'upTo', max: 100 } },
  { key: 'dailyLossPct', label: 'Daily loss limit (%)', unit: '%', step: 'any', kind: { type: 'upTo', max: 100 } },
  { key: 'weeklyLossR', label: 'Weekly loss limit (R)', unit: 'R', step: 'any', kind: { type: 'upTo', max: 100 } },
] as const satisfies readonly { key: keyof RiskProfileInput; label: string; unit: string; step: string; kind: Kind }[];

export type RiskNumberKey = (typeof riskFields)[number]['key'];

export interface RiskProfileForm extends Record<RiskNumberKey, string> {
  name: string;
  weeklyLossAction: string;
}

/** Values a brand-new profile starts with (modest, inside every limit). */
const startingValues: Record<RiskNumberKey, number> = {
  riskPerTradePct: 0.5,
  maxCapitalPerTradePct: 20,
  maxNewEntriesPerDay: 3,
  maxOpenPositions: 5,
  maxTotalRiskPct: 3,
  maxPerSector: 2,
  maxPairCorrelation: 0.8,
  maxIntraday: 3,
  maxSwing: 5,
  dailyLossR: 3,
  dailyLossPct: 1.5,
  weeklyLossR: 6,
};

export function riskProfileFormFrom(source?: RiskProfile, name = ''): RiskProfileForm {
  const values = source ?? { ...startingValues, weeklyLossAction: 'halve_size' };
  const form: Record<string, string> = { name, weeklyLossAction: values.weeklyLossAction };
  for (const { key } of riskFields) {
    form[key] = String(values[key]);
  }
  return form as unknown as RiskProfileForm;
}

export function toRiskProfileInput(form: RiskProfileForm): RiskProfileInput {
  const input: Record<string, string | number | null> = {
    name: form.name.trim(),
    weeklyLossAction: form.weeklyLossAction,
  };
  for (const { key } of riskFields) {
    input[key] = parseNumber(form[key]) ?? null;
  }
  return input as unknown as RiskProfileInput;
}

export function validateRiskProfile(form: RiskProfileForm): FieldErrors {
  const errors: Record<string, string> = {};
  const name = form.name.trim();
  if (name.length === 0 || name.length > 60) {
    errors.name = 'Required, at most 60 characters.';
  }
  for (const { key, kind } of riskFields) {
    const value = parseNumber(form[key]);
    const bad = value === undefined || !Number.isFinite(value);
    if (kind.type === 'upTo' && (bad || !(value > 0 && value <= kind.max))) {
      errors[key] =
        kind.max === 100 ? 'Use a value above 0 and at most 100.' : `Use a value above 0 and at most ${kind.max}.`;
    } else if (kind.type === 'count' && (bad || !Number.isInteger(value) || value < 1 || value > 100)) {
      errors[key] = 'Use a whole number from 1 to 100.';
    } else if (kind.type === 'ratio' && (bad || value < 0 || value > 1)) {
      errors[key] = 'Use a value from 0 to 1.';
    }
  }
  const risk = parseNumber(form.riskPerTradePct);
  const total = parseNumber(form.maxTotalRiskPct);
  if (!errors.maxTotalRiskPct && risk !== undefined && total !== undefined && total < risk) {
    errors.maxTotalRiskPct = 'Must be at least the risk per trade.';
  }
  return errors;
}

/** Why a profile cannot be deleted (undefined when it can). The server enforces the same rules. */
export function deleteBlockReason(profile: RiskProfile): string | undefined {
  if (profile.isSeeded) {
    return 'Built-in profiles cannot be deleted.';
  }
  if (profile.accountCount > 0) {
    return `Used by ${profile.accountCount} account${profile.accountCount === 1 ? '' : 's'} — move them to another profile first.`;
  }
  return undefined;
}
