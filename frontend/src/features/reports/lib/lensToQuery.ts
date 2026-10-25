export const lensIds = ['all', 'basket', 'instrument', 'strategy', 'trade', 'account'] as const;
export type Lens = (typeof lensIds)[number];

export const lensOptions: readonly { id: Lens; label: string }[] = [
  { id: 'all', label: 'All trades' },
  { id: 'basket', label: 'By basket' },
  { id: 'instrument', label: 'By instrument' },
  { id: 'strategy', label: 'By strategy' },
  { id: 'trade', label: 'By individual trade' },
  { id: 'account', label: 'By account' },
];

/** What a lens asks: the summary, a groups table `by` one dimension, or nothing yet (with the reason). */
export type LensTarget =
  | { kind: 'summary' }
  | { kind: 'groups'; by: 'basket' | 'instrument' | 'strategy' }
  | { kind: 'later'; reason: string }
  | { kind: 'refused'; dimension: 'account' };

/** A lens is a mapping onto the groups `lens` parameter, not a second query model (P8-09 decision 8). */
export function lensToQuery(lens: Lens): LensTarget {
  switch (lens) {
    case 'all':
      return { kind: 'summary' };
    case 'trade':
      return { kind: 'later', reason: 'By individual trade is coming with P8-11.' };
    case 'account':
      return { kind: 'refused', dimension: 'account' };
    default:
      return { kind: 'groups', by: lens };
  }
}

export const readLens = (value: string | null): Lens => lensIds.find((id) => id === value) ?? 'all';
