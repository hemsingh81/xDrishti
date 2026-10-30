import { queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type ReadinessAccount = Schemas['ExecutionReadinessAccount'];
export type ReadinessCheck = Schemas['ExecutionReadinessCheck'];

export const readinessKey = ['execution', 'readiness'] as const;

/** Whether execution can act today, per account. Read only; the server decides, the screen only shows. */
export const readinessQuery = queryOptions({
  queryKey: readinessKey,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/execution/readiness', { signal })),
  refetchInterval: 30_000,
});

/** "Check now": leaves a command for the execution service; the new record shows in the next read. */
export function useCheckNow() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/execution/readiness/checks')),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: readinessKey }),
  });
}

export function useRecordAttestation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: Schemas['IpAttestationRequest']) =>
      unwrap(await api.POST('/api/execution/ip-attestations', { body })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: readinessKey }),
  });
}

/** Plans of the session day and the services list, for the morning checklist's last two lines. */
export const todayPlansQuery = (date: string) =>
  queryOptions({
    queryKey: ['execution', 'plans', date] as const,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/plans', { params: { query: { date } }, signal })),
  });

export const feedStatusQuery = queryOptions({
  queryKey: ['execution', 'feed'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/system/status', { signal })),
});
