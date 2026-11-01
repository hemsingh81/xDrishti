import { queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { api, unwrap, type Schemas } from '@/shared/api';

export type SettingsView = Schemas['ExecutionSettingsView'];
export type DrillsView = Schemas['ExecutionDrillsView'];

/** The global switches: the row in force, the waiting raise, the last rows. */
export const settingsQuery = queryOptions({
  queryKey: ['execution', 'settings'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/execution/settings', { signal })),
});

// shortcut: polls every 3 s while a request has no answer (bounded by unmount and background tabs); add an age cut-off if xd-exec outages are common.
const waiting = (data: DrillsView | undefined) => data?.requests.some((r) => r.result === null) === true;

/** The drills and the requests with xd-exec's answer; asked again every 3 s while one request has no answer. */
export const drillsQuery = queryOptions({
  queryKey: ['execution', 'drills'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/execution/drills', { signal })),
  refetchInterval: (query) => (waiting(query.state.data) ? 3000 : false),
});

/** A mutation that never keeps the typed password: no cache time, and reset when the component goes away. */
function useForgetful<T extends { reset: () => void }>(mutation: T): T {
  const { reset } = mutation;
  useEffect(() => reset, [reset]);
  return mutation;
}

export function useChangeSettings() {
  const client = useQueryClient();
  return useForgetful(
    useMutation({
      gcTime: 0,
      mutationFn: async (body: Schemas['ExecutionSettingsChangeBody']) =>
        unwrap(await api.PUT('/api/execution/settings', { body })),
      onSuccess: () => client.invalidateQueries({ queryKey: ['execution'] }),
    }),
  );
}

export function useRequestDrill() {
  const client = useQueryClient();
  return useForgetful(
    useMutation({
      gcTime: 0,
      mutationFn: async (body: Schemas['DrillRequestBody']) =>
        unwrap(await api.POST('/api/execution/drills', { body })),
      onSuccess: () => client.invalidateQueries({ queryKey: ['execution', 'drills'] }),
    }),
  );
}
