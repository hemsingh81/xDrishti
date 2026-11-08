import { queryOptions } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type ShadowSummary = Schemas['ShadowSummaryView'];
export type ShadowGroup = Schemas['ShadowGroupView'];
export type ShadowSequential = Schemas['ShadowSequentialView'];
export type SequentialStatus = Schemas['SequentialStatusView'];

// GETs only: the Shadow tab is read only. (The switch has no control here; the owner uses the CLI or the API.)
export const shadowSummaryQuery = queryOptions({
  queryKey: ['paper', 'shadow-summary'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/paper/shadow/summary', { signal })),
});

// 404 paper.not_found means there is no shadow edge series yet; the caller shows that as "no test yet".
export const paperSequentialQuery = queryOptions({
  queryKey: ['paper', 'sequential'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/paper/sequential', { signal })),
  retry: false,
});
