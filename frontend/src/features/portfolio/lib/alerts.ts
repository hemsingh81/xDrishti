import type { PriceAlert, PriceAlertInput } from '@/shared/api';
import { formatInrPrecise } from '@/shared/lib/format';

export const alertStatuses = ['active', 'triggered', 'cancelled'] as const;
export type AlertStatus = (typeof alertStatuses)[number];

export const alertStatusLabels: Record<AlertStatus, string> = {
  active: 'Active',
  triggered: 'Triggered',
  cancelled: 'Cancelled',
};

/** The ids that tie a tab of the Alerts panel to its panel. */
export const alertTabId = (status: AlertStatus) => `alerts-tab-${status}`;
export const alertPanelId = (status: AlertStatus) => `alerts-panel-${status}`;

export const isAlertStatus = (value: string | null): value is AlertStatus => alertStatuses.some((s) => s === value);

export const alertDirections = ['at_or_below', 'at_or_above'] as const;
export type AlertDirection = (typeof alertDirections)[number];

export const directionLabels: Record<AlertDirection, string> = {
  at_or_below: 'At or below',
  at_or_above: 'At or above',
};

/** How many active alerts the API allows (the page of alerts carries it too; this is for the dialog's refusal text). */
export const defaultAlertLimit = 200;
export const maxNoteLength = 200;
export const maxAlertLevel = 10_000_000;

/** "at or below ₹1,200.00": the condition in words. */
export function conditionText(alert: Pick<PriceAlert, 'direction' | 'level'>): string {
  const direction = alert.direction === 'at_or_above' ? 'at or above' : 'at or below';
  return `${direction} ${formatInrPrecise(alert.level)}`;
}

/** The checking in words: said in the dialog, the drawer and the Action center. */
export const checkingText =
  'Checked every 5 minutes while the market is open, on the latest price at that moment: a move between two checks is not seen. An alert triggers once, then stays in the Triggered list.';

/** Quick levels around the last price: a percentage below (for "at or below") or above (for "at or above"). */
export const presetPercents = [5, 10, 20] as const;

/** The level for a preset: the last price moved by `percent` % in the direction of the alert, to 2 decimals (rounded to the paisa). */
export function presetLevel(direction: AlertDirection, lastPrice: number, percent: number): number {
  const factor = direction === 'at_or_below' ? 1 - percent / 100 : 1 + percent / 100;
  return Math.round(lastPrice * factor * 100) / 100;
}

export interface AlertForm {
  instrumentId: string;
  direction: AlertDirection;
  level: string;
  note: string;
}

export type AlertFormErrors = Partial<Record<keyof AlertForm, string>>;

/** Parses what was typed as a price: digits with an optional decimal point (thousands separators and a ₹ sign are allowed). */
export function parseLevel(text: string): number | null {
  const cleaned = text.replace(/[₹,\s]/g, '');
  if (!/^\d+(\.\d+)?$/.test(cleaned)) return null;
  return Number(cleaned);
}

/** The same checks as the API (the API has the last word): an instrument, a level above 0 with at most 4 decimals, a short note. */
export function validateAlert(form: AlertForm): AlertFormErrors {
  const errors: AlertFormErrors = {};
  if (form.instrumentId === '') errors.instrumentId = 'Choose a share or ETF.';
  const level = parseLevel(form.level);
  if (form.level.trim() === '') errors.level = 'Enter the price to be alerted at.';
  else if (level === null || !(level > 0)) errors.level = 'Enter a price above 0, e.g. 1200 or 1200.50.';
  else if (level > maxAlertLevel) errors.level = 'The price can be at most ₹1,00,00,000.';
  else if (/\.\d{5,}$/.test(form.level.trim())) errors.level = 'Use at most 4 decimals.';
  if (form.note.length > maxNoteLength) errors.note = `At most ${String(maxNoteLength)} characters.`;
  return errors;
}

export function toAlertInput(form: AlertForm): PriceAlertInput {
  return {
    instrumentId: form.instrumentId,
    direction: form.direction,
    level: parseLevel(form.level),
    note: form.note.trim() === '' ? null : form.note.trim(),
  };
}
