import { queryOptions } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type EpochList = Schemas['EpochList'];
export type EpochView = Schemas['EpochView'];
export type ConfigView = Schemas['ConfigView'];
export type QueueView = Schemas['QueueView'];

export const governanceKeys = {
  epochs: ['paper', 'epochs'] as const,
  config: ['paper', 'config'] as const,
  queue: ['paper', 'queue'] as const,
};

/** The measurement epochs, newest first (read only). */
export const epochsQuery = queryOptions({
  queryKey: governanceKeys.epochs,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/paper/epochs', { signal })),
});

/** The configuration now, the last snapshot, the differences not yet captured and the blockers (a preview; stores nothing). */
export const configQuery = queryOptions({
  queryKey: governanceKeys.config,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/paper/config', { signal })),
});

/** Accepted hypotheses that no change names (read only; nothing is applied). */
export const queueQuery = queryOptions({
  queryKey: governanceKeys.queue,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/paper/queue', { signal })),
});
