import { queryOptions } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type LivePhaseCheckView = Schemas['LivePhaseCheckView'];
export type LivePhaseReport = Schemas['LivePhaseReport'];

/** The small-live phase check, recomputed on every read (read only; the server picks the one live account and the last closed session). */
export const phaseCheckQuery = queryOptions({
  queryKey: ['live', 'phase-check'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/live-trading/phase-check', { signal })),
  retry: false,
});
