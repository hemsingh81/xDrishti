import { queryOptions } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api';

/** The P14 families whose newest stored verdict the section shows, in order (spec P14-16; the BASIS family is cut by default). */
export const p14Families = [
  'expr:option_buy',
  'expr:debit_spread',
  'expr:credit_spread',
  'expr:iron_condor',
  'pairs:intraday-1',
  'ext:iv-1',
] as const;

/** The newest stored expression runs (bounded: 20). */
export const expressionRunsQuery = queryOptions({
  queryKey: ['learning', 'expression-runs'] as const,
  queryFn: async ({ signal }) =>
    unwrap(await api.GET('/api/research/expression-runs', { params: { query: { limit: 20 } }, signal })),
});

/** The stored pair formations of the default range (the server bounds it and says when it truncated). */
export const pairFormationsQuery = queryOptions({
  queryKey: ['learning', 'pair-formations'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/research/pair-formations', { signal })),
});
