import type { ExtensionEvaluation, ExtensionFamily } from '../api/extensionQueries';

export const familyTitles: Readonly<Record<ExtensionFamily, string>> = {
  'filter:results': 'Event filter (results within N sessions)',
  'ext:ev-1': 'Event features ev-1',
  'ext:fu-1': 'Fundamental features fu-1',
  'ext:llm-1': 'Announcement labels llm-1',
  pead: 'Post-results drift (PEAD)',
};

const record = (value: unknown): Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Record<string, unknown>) : {};

/** A number in the stored JSON, or null when it is the text `unavailable` (or anything else): never a zero. */
export const numberOf = (value: unknown): number | null =>
  typeof value === 'number' && Number.isFinite(value) ? value : null;

export const estimatesOf = (e: ExtensionEvaluation) => record(e.estimates);

/** The window N of a filter record (its version key ends `:N=<n>`), else null. */
export function windowOf(e: ExtensionEvaluation): number | null {
  const match = /:N=(\d+)$/.exec(e.versionKey);
  return match?.[1] ? Number(match[1]) : null;
}

/** The newest record of each version key and kind (development or hold-out): the list is newest first, so the first seen wins. */
export function latestPerKey(items: readonly ExtensionEvaluation[]): ExtensionEvaluation[] {
  const seen = new Set<string>();
  return items.filter((e) => {
    const key = `${e.versionKey}|${String(e.holdOutRead)}`;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

export interface ReasonShare {
  reason: string;
  trades: number | null;
  share: number | null;
}

export interface FilterShares {
  trades: number | null;
  skipped: number | null;
  taken: number | null;
  unavailable: number | null;
  skippedShare: number | null;
  takenShare: number | null;
  unavailableShare: number | null;
  lostToNoUpcomingEntry: number | null;
  byReason: ReasonShare[];
}

/** The shares of protocol F (skipped, taken, unavailable by reason, and the share lost to `no_upcoming_entry`). */
export function filterShares(e: ExtensionEvaluation): FilterShares {
  const est = estimatesOf(e);
  const by = record(est.unavailable_by_reason);
  return {
    trades: numberOf(est.trades),
    skipped: numberOf(est.skipped),
    taken: numberOf(est.taken),
    unavailable: numberOf(est.unavailable),
    skippedShare: numberOf(est.skipped_share),
    takenShare: numberOf(est.taken_share),
    unavailableShare: numberOf(est.unavailable_share),
    lostToNoUpcomingEntry: numberOf(est.share_lost_to_no_upcoming_entry),
    byReason: Object.entries(by).map(([reason, v]) => ({
      reason,
      trades: numberOf(record(v).trades),
      share: numberOf(record(v).share),
    })),
  };
}

export interface BoundLine {
  label: string;
  /** Null when the key is absent or not a number (the stored `unavailable`): shown as a word, never as zero. */
  value: number | null;
  unit: 'R' | 'share';
}

/**
 * The figures a verdict was judged on, by protocol and reading, with the key names the engine stores (ProtocolF, ProtocolX
 * via ExtensionReport, ProtocolP). Protocol L stores no bound.
 */
export function boundsOf(e: ExtensionEvaluation): BoundLine[] {
  const est = estimatesOf(e);
  const line = (label: string, key: string, unit: BoundLine['unit'] = 'R'): BoundLine => ({
    label,
    value: numberOf(est[key]),
    unit,
  });
  switch (e.protocol) {
    case 'F':
      return e.holdOutRead
        ? [
            line('Mean R of skipped trades', 'mean_r_skipped'),
            line('Mean R of taken trades', 'mean_r_taken'),
            line('Tail share of skipped trades', 'tail_share_skipped', 'share'),
            line('Tail share of taken trades', 'tail_share_taken', 'share'),
          ]
        : [
            line('Mean gap', 'mean_gap_r'),
            line('Lower bound', 'mean_gap_lower'),
            line('Upper bound', 'mean_gap_upper'),
            line('Required', 'mean_gap_required'),
          ];
    case 'X':
      return [line('Change in R', 'delta_r'), line('Lower bound', 'lower'), line('Upper bound', 'upper')];
    case 'P':
      return e.holdOutRead
        ? [line('Hold-out gain', 'hold_out_gain_r'), line('Test gain', 'test_gain_r')]
        : [
            line('Test gain', 'test_gain_r'),
            line('Lower bound', 'lower'),
            line('Upper bound', 'upper'),
            line('Required', 'required'),
          ];
    default:
      return [];
  }
}
