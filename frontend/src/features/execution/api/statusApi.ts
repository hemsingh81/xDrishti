import { queryOptions } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type ExecutionStatus = Schemas['ExecutionStatusView'];
export type ExecutionNotice = Schemas['ExecutionNotice'];

/** The server's own cadence (`pollSeconds`: 5 while something is automated and the market is open, else 60); a hidden tab asks every 60 s. */
export function pollIntervalMs(status: ExecutionStatus | undefined, hidden: boolean): number {
  const seconds = hidden ? 60 : (status?.pollSeconds ?? 60);
  return Math.max(seconds, 1) * 1000;
}

const hidden = () => document.visibilityState === 'hidden';

/** The service, the gate per account and the four fields the app bar and the bell need. */
export const executionStatusQuery = queryOptions({
  queryKey: ['execution', 'status'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/execution/status', { signal })),
  refetchInterval: (query) => pollIntervalMs(query.state.data, hidden()),
  refetchIntervalInBackground: true,
});

/** The bell's execution items, derived on the server from insert-only rows (spec P12-10 D5). */
export const executionNoticesQuery = queryOptions({
  queryKey: ['execution', 'notices'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/execution/notices', { signal })),
});
