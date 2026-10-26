import { queryOptions } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type PaperPhaseReport = Schemas['PaperPhaseReport'];
export type PhaseCriterion = Schemas['PhaseCriterion'];

// A GET like every query of the Paper screen: the check is computed on read from stored records and nothing is written.
export const paperPhaseQuery = queryOptions({
  queryKey: ['paper', 'phase-check'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/paper/phase-check', { signal })),
});
