import type { ActionThresholds, ActionThresholdsInput } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';

export type ThresholdKey = keyof ActionThresholds;

export interface ThresholdField {
  key: ThresholdKey;
  /** The label with its unit: "Loss from cost (%)". */
  label: string;
  min: number;
  max: number;
  /** Whole numbers only. */
  integer: boolean;
  /** At most this many decimals (0 for whole numbers). */
  decimals: number;
}

/** The ten thresholds with the ranges of ADR 0022 (the API checks the same ranges and has the last word). */
export const thresholdFields: readonly ThresholdField[] = [
  { key: 'belowCostPct', label: 'Loss from cost (%)', min: 1, max: 90, integer: false, decimals: 2 },
  { key: 'belowCostHighPct', label: 'Loss from cost, high severity (%)', min: 2, max: 95, integer: false, decimals: 2 },
  { key: 'gainPct', label: 'Gain on cost (%)', min: 1, max: 1000, integer: false, decimals: 2 },
  { key: 'overweightFloorPct', label: 'Share of the account (%)', min: 1, max: 100, integer: false, decimals: 2 },
  { key: 'overweightMultiple', label: 'Times an equal share (×)', min: 1, max: 10, integer: false, decimals: 2 },
  { key: 'minPositions', label: 'Positions in the account', min: 2, max: 50, integer: true, decimals: 0 },
  { key: 'concentrationPct', label: 'Sector share of the account (%)', min: 5, max: 100, integer: false, decimals: 2 },
  { key: 'lagPp', label: 'Behind Nifty 50 by (pp)', min: 1, max: 100, integer: false, decimals: 2 },
  { key: 'lagDays', label: 'Held for more than (days)', min: 30, max: 3650, integer: true, decimals: 0 },
  {
    key: 'longTermWindowDays',
    label: 'Turns 12 months old within (days)',
    min: 1,
    max: 365,
    integer: true,
    decimals: 0,
  },
];

export interface ThresholdGroup {
  title: string;
  description: string;
  keys: readonly ThresholdKey[];
}

/** The fields by rule, so each rule's numbers sit together with a sentence saying what they do. */
export const thresholdGroups: readonly ThresholdGroup[] = [
  {
    title: 'Below cost',
    description:
      'A holding down this much from what you paid is flagged; down by the higher figure it is flagged as high.',
    keys: ['belowCostPct', 'belowCostHighPct'],
  },
  {
    title: 'Large gain',
    description: 'A holding up this much from what you paid is flagged for a look.',
    keys: ['gainPct'],
  },
  {
    title: 'Overweight',
    description:
      'A holding is overweight when its share of the account is above the larger of the share below and the multiple of an equal share, in accounts with at least this many positions.',
    keys: ['overweightFloorPct', 'overweightMultiple', 'minPositions'],
  },
  {
    title: 'Sector concentration',
    description: 'One sector above this share of an account is flagged.',
    keys: ['concentrationPct'],
  },
  {
    title: 'Lagging Nifty',
    description:
      'A holding held longer than this whose XIRR is behind the same money in Nifty 50 by more than this is flagged.',
    keys: ['lagPp', 'lagDays'],
  },
  {
    title: 'Long term soon',
    description: 'A lot in profit that turns 12 months old within this many days is flagged (information only).',
    keys: ['longTermWindowDays'],
  },
];

export type ThresholdForm = Record<ThresholdKey, string>;
export type ThresholdErrors = Partial<Record<ThresholdKey, string>>;

export function formFrom(v: ActionThresholds): ThresholdForm {
  return {
    belowCostPct: String(v.belowCostPct),
    belowCostHighPct: String(v.belowCostHighPct),
    gainPct: String(v.gainPct),
    overweightFloorPct: String(v.overweightFloorPct),
    overweightMultiple: String(v.overweightMultiple),
    minPositions: String(v.minPositions),
    concentrationPct: String(v.concentrationPct),
    lagPp: String(v.lagPp),
    lagDays: String(v.lagDays),
    longTermWindowDays: String(v.longTermWindowDays),
  };
}

/** A number typed in a field: digits with an optional decimal point; null for anything else (empty, text, "1e3"). */
export function parseThreshold(text: string): number | null {
  const cleaned = text.trim();
  return /^\d+(\.\d+)?$/.test(cleaned) ? Number(cleaned) : null;
}

const rangeText = (field: ThresholdField): string => `${formatNumber(field.min)} to ${formatNumber(field.max)}`;

/** "1–90" for the helper text. */
export const rangeHint = (field: ThresholdField): string =>
  `${formatNumber(field.min)}–${formatNumber(field.max)}${field.integer ? ', whole number' : ''}`;

function fieldError(field: ThresholdField, text: string): string | undefined {
  const value = parseThreshold(text);
  if (value === null)
    return field.integer
      ? `Enter a whole number from ${rangeText(field)}.`
      : `Enter a number from ${rangeText(field)}.`;
  if (value < field.min || value > field.max) return `Use a value from ${rangeText(field)}.`;
  if (field.integer && !Number.isInteger(value)) return `Use a whole number from ${rangeText(field)}.`;
  if (!field.integer && /\.\d{3,}$/.test(text.trim())) return 'Use at most 2 decimals.';
  return undefined;
}

/** The checks of the API, so a wrong value is seen before Save (the API has the last word and may add its own). */
export function validateThresholds(form: ThresholdForm): ThresholdErrors {
  const errors: ThresholdErrors = {};
  for (const field of thresholdFields) {
    const message = fieldError(field, form[field.key]);
    if (message) errors[field.key] = message;
  }
  const low = parseThreshold(form.belowCostPct);
  const high = parseThreshold(form.belowCostHighPct);
  if (!errors.belowCostHighPct && low !== null && high !== null && high <= low) {
    errors.belowCostHighPct = `Must be greater than the loss threshold (${formatNumber(low)} %).`;
  }
  return errors;
}

export function toInput(f: ThresholdForm): ActionThresholdsInput {
  return {
    belowCostPct: parseThreshold(f.belowCostPct),
    belowCostHighPct: parseThreshold(f.belowCostHighPct),
    gainPct: parseThreshold(f.gainPct),
    overweightFloorPct: parseThreshold(f.overweightFloorPct),
    overweightMultiple: parseThreshold(f.overweightMultiple),
    minPositions: parseThreshold(f.minPositions),
    concentrationPct: parseThreshold(f.concentrationPct),
    lagPp: parseThreshold(f.lagPp),
    lagDays: parseThreshold(f.lagDays),
    longTermWindowDays: parseThreshold(f.longTermWindowDays),
  };
}

/** Whether the form holds exactly these values (typed "12.50" equals 12.5). */
export function sameValues(form: ThresholdForm, values: ActionThresholds): boolean {
  return thresholdFields.every(({ key }) => parseThreshold(form[key]) === values[key]);
}
