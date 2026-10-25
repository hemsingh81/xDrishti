import type { Schemas } from '@/shared/api';

export type StoredFlags = Schemas['PlanFlagsView'];

const gateText = (f: StoredFlags): string => {
  switch (f.gate) {
    case 'unavailable':
      return f.gateMissing.length > 0
        ? `Market gate: unavailable (${f.gateMissing.join(', ')} not known)`
        : 'Market gate: unavailable';
    case 'no_trade':
      return 'Market gate: no-trade day';
    default:
      return `Market gate: ${f.gate}`;
  }
};

const openBookText: Record<string, string> = {
  known: 'Open book: known',
  assumed_empty: 'Open book: assumed empty by owner setting',
  unavailable: 'Open book: unavailable',
};

const costText: Record<string, string> = {
  gross: 'Cost basis: gross, before charges',
  costed_unconfirmed: 'Cost basis: costed, sheet not confirmed as yours',
  net_confirmed: 'Cost basis: net of charges, sheet confirmed',
  mixed: 'Cost basis: mixed across the tickets',
  unknown: 'Cost basis: not known',
};

/** One chip of the stored flags: the words, and whether it departs from the plain case (a warning look; the words carry the meaning). */
export interface FlagChip {
  key: string;
  label: string;
  notable: boolean;
}

/** The chips of a plan's stored flags, in a fixed order, as text. Pure. */
export function flagChips(f: StoredFlags): FlagChip[] {
  const chips: FlagChip[] = [
    { key: 'gate', label: gateText(f), notable: f.gate !== 'normal' && f.gate !== 'off' },
    {
      key: 'throttle',
      label: `Throttle: ${f.throttle}`,
      notable: f.throttle === 'cut' || f.throttle === 'unavailable',
    },
    { key: 'book', label: openBookText[f.openBook] ?? `Open book: ${f.openBook}`, notable: f.openBook !== 'known' },
  ];
  if (Math.abs(f.riskMultiplier - 1) > 1e-9) {
    chips.push({ key: 'risk', label: `Risk multiplier ${String(Number(f.riskMultiplier.toFixed(3)))}`, notable: true });
  }
  if (f.correlation !== 'applied' || f.correlationUnavailablePairs > 0) {
    chips.push({
      key: 'corr',
      label:
        f.correlationUnavailablePairs > 0
          ? `Correlation: ${String(f.correlationUnavailablePairs)} pair(s) not checked`
          : `Correlation: ${f.correlation}`,
      notable: f.correlation === 'unavailable' || f.correlationUnavailablePairs > 0,
    });
  }
  if (f.ruleRemovedHalf) {
    chips.push({ key: 'half', label: 'A rule removed half or more of its candidates', notable: true });
  }
  chips.push({
    key: 'cost',
    label: costText[f.costBasis] ?? `Cost basis: ${f.costBasis}`,
    notable: f.costBasis !== 'net_confirmed',
  });
  return chips;
}

/** The gate's own sentence when it cancelled the plan's tickets. */
export const noTradeSentence = 'Market gate: no-trade day, tickets moved to reserve';

export const legacyFlagsLabel = 'Read from the basis text; this plan predates structured flags';
