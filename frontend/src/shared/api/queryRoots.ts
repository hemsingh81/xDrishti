/**
 * The first parts of the query keys that more than one feature touches. A feature builds its own keys below these
 * roots; another feature that has to reload them (a tracking change shows in the baskets' members, a basket change
 * shows in the instruments' lists) invalidates by the root and never imports the other feature.
 */
export const queryRoots = {
  /** Everything the Data & import screen reads (and the data jobs the Data quality screen starts and watches). */
  data: ['data'] as const,
  /** Everything the Data quality screen reads (a corporate action changes its pending count). */
  quality: ['quality'] as const,
  /** Everything the Instruments screen reads: search results, counts, details, refresh status. */
  instruments: ['instruments'] as const,
  /** Everything the Services screen reads (the list, the timeline, the runs, the bell's notices). */
  services: ['services'] as const,
  /** The Action center's lists (the Settings thresholds card reloads them: a threshold changes the next read). */
  portfolioActions: ['portfolio', 'actions'] as const,
  /** Every members list (current and as-of) of every basket. */
  basketMembers: ['baskets', 'members'] as const,
};
