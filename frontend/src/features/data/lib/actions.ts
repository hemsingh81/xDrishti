import type { CorporateAction } from '@/shared/api';
import { formatDate } from '@/shared/lib/format';

/** The states of a corporate action (ADR 0017 §5); the card filters by one of them or shows all. */
export const actionStatuses = ['suggested', 'applied', 'ignored'] as const;
export type ActionStatus = (typeof actionStatuses)[number];

export const actionsViews = ['all', ...actionStatuses] as const;
export type ActionsView = (typeof actionsViews)[number];

export const actionsViewLabels: Readonly<Record<ActionsView, string>> = {
  all: 'All',
  suggested: 'Suggested',
  applied: 'Applied',
  ignored: 'Ignored',
};

export const isActionsView = (value: string | null): value is ActionsView => actionsViews.some((v) => v === value);

export const actionKinds = ['split', 'bonus'] as const;
export type ActionKind = (typeof actionKinds)[number];

export const isActionKind = (value: string): value is ActionKind => actionKinds.some((k) => k === value);

export const kindLabels: Readonly<Record<ActionKind, string>> = { split: 'Split', bonus: 'Bonus' };

export const maxActionNote = 200;
export const maxRatioPart = 100_000;
/** The most an import takes in one go (the API refuses a longer text). */
export const maxImportLines = 5000;

const sourceLabels: Readonly<Record<string, string>> = { manual: 'You', detected: 'Detected', csv: 'CSV import' };
export const actionSourceLabel = (source: string): string => sourceLabels[source] ?? source;

export const kindLabel = (kind: string): string => (isActionKind(kind) ? kindLabels[kind] : kind);

/** True when an action with this ex-date would adjust bars that the source already adjusted (the day the CSV files reach). */
export const adjustsCsvTwice = (exDate: string, through: string): boolean => exDate !== '' && exDate <= through;

/**
 * The price multiplier for the bars before the ex-date. A split n:d (old shares : new shares) is n/d, a bonus a:b
 * (a bonus shares for b held) is b/(a+b). Null when the numbers are not whole and positive.
 */
export function factorOf(kind: string, numerator: number, denominator: number): number | null {
  if (!Number.isInteger(numerator) || !Number.isInteger(denominator) || numerator < 1 || denominator < 1) return null;
  if (kind === 'split') return numerator / denominator;
  if (kind === 'bonus') return denominator / (numerator + denominator);
  return null;
}

const factorFormat = new Intl.NumberFormat('en-IN', { maximumFractionDigits: 4 });

/** "×0.2": a factor as it is said (up to four decimals, as the API rounds prices). */
export const factorText = (factor: number): string => `×${factorFormat.format(factor)}`;

/** "Prices before 1 Sep 2026 × 0.2". */
export const factorPreview = (exDate: string, factor: number): string =>
  `Prices before ${formatDate(exDate)} × ${factorFormat.format(factor)}`;

/** "Adjusted by ×0.2 for 1 action". */
export const adjustedText = (factor: number, actions: number): string =>
  `Adjusted by ${factorText(factor)} for ${String(actions)} ${actions === 1 ? 'action' : 'actions'}`;

/** What the ratio means, in words, for the form and the help text. */
export const ratioHelp: Readonly<Record<ActionKind, string>> = {
  split: 'Split n:d is old shares : new shares, so 1:5 turns one share into five and prices before the ex-date × 0.2.',
  bonus:
    'Bonus a:b is a bonus shares for b held, so 1:1 gives one extra share for each held and prices before the ex-date × 0.5.',
};

/** The name a row has in buttons and toasts: "NSE:INFY split 1:5 on 1 Sep 2026". */
export const actionName = (action: Pick<CorporateAction, 'symbol' | 'kind' | 'ratio' | 'exDate'>): string =>
  `${action.symbol} ${action.kind} ${action.ratio} on ${formatDate(action.exDate)}`;
