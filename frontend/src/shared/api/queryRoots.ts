/**
 * The first parts of the query keys that more than one feature touches. A feature builds its own keys below these
 * roots; another feature that has to reload them (a tracking change shows in the baskets' members, a basket change
 * shows in the instruments' lists) invalidates by the root and never imports the other feature.
 */
export const queryRoots = {
  /** Everything the Instruments screen reads: search results, counts, details, refresh status. */
  instruments: ['instruments'] as const,
  /** Every members list (current and as-of) of every basket. */
  basketMembers: ['baskets', 'members'] as const,
};
