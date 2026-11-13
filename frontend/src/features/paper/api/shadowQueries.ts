import { queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type ShadowSummary = Schemas['ShadowSummaryView'];
export type ShadowGroup = Schemas['ShadowGroupView'];
export type ShadowSequential = Schemas['ShadowSequentialView'];
export type SequentialStatus = Schemas['SequentialStatusView'];
export type EvidenceSet = Schemas['EvidenceSetView'];

/** The owner's change of the shadow switch (OPS-01): the password is typed for this act and lives only in the call's variables. */
export function useShadowSwitch() {
  const queryClient = useQueryClient();
  return useMutation({
    gcTime: 0,
    mutationFn: async (body: { enabled: boolean; reason: string; password: string; acknowledgeGap: boolean }) =>
      unwrap(await api.PUT('/api/paper/shadow/settings', { body })),
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['paper'] }),
  });
}

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

export const evidenceSetQuery = queryOptions({
  queryKey: ['paper', 'shadow-evidence-set'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/paper/shadow/evidence-set', { signal })),
});

/** The owner's replacement of the evidence set (AM-01b, ADR 0282): the password is typed for this act and lives only in the call's variables. */
export function useReplaceEvidenceSet() {
  const queryClient = useQueryClient();
  return useMutation({
    gcTime: 0,
    mutationFn: async (body: { versions: string[]; reason: string; password: string }) =>
      unwrap(await api.PUT('/api/paper/shadow/evidence-set', { body })),
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['paper'] }),
  });
}
