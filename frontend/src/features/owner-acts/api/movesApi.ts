import { queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import { api, unwrap, type Schemas } from '@/shared/api';

export type LessonRecord = Schemas['LessonView'];
export type HypothesisRecord = Schemas['HypothesisView'];
export type ChangelogRecord = Schemas['ChangelogEntryView'];
export type AutonomyRecord = Schemas['AutonomySettingsView'];

export const movesKeys = {
  lesson: (id: number) => ['owner-acts', 'lesson', id] as const,
  hypothesis: (id: number) => ['owner-acts', 'hypothesis', id] as const,
  entry: (id: number) => ['owner-acts', 'entry', id] as const,
  autonomy: ['owner-acts', 'autonomy'] as const,
};

// The reads of the forms: the record the move is about, with the states the server says it may move to.
export const lessonQuery = (id: number) =>
  queryOptions({
    queryKey: movesKeys.lesson(id),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/lessons/{id}', { params: { path: { id } }, signal })),
    staleTime: 0,
  });

export const hypothesisQuery = (id: number) =>
  queryOptions({
    queryKey: movesKeys.hypothesis(id),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/hypotheses/{id}', { params: { path: { id } }, signal })),
    staleTime: 0,
  });

export const entryQuery = (id: number) =>
  queryOptions({
    queryKey: movesKeys.entry(id),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/changelog/{id}', { params: { path: { id } }, signal })),
    staleTime: 0,
  });

/** The settings the form is loaded at. They are kept as they were read (no refetch under the owner's hands): a change made elsewhere is a 409 `autonomy.stale`, not a silent reset. */
export const autonomySettingsQuery = queryOptions({
  queryKey: movesKeys.autonomy,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/autonomy/settings', { signal })),
  staleTime: Infinity,
  refetchOnWindowFocus: false,
});

/**
 * One owner act (a write). The password is in the variables of the call: the mutation is dropped as soon as it ends (`gcTime` 0) and reset
 * on settle, and the caller clears the field. Whatever the answer, the records this act can have changed are read again.
 */
function useAct<Input, Output>(call: (input: Input) => Promise<Output>) {
  const queryClient = useQueryClient();
  const mutation = useMutation({
    gcTime: 0,
    mutationFn: call,
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: ['learning'] });
      void queryClient.invalidateQueries({ queryKey: ['plans', 'autonomy'] });
      void queryClient.invalidateQueries({ queryKey: ['owner-acts'] });
    },
  });
  const { mutateAsync, reset } = mutation;
  const run = useCallback(
    async (input: Input) => {
      try {
        return await mutateAsync(input);
      } finally {
        reset();
      }
    },
    [mutateAsync, reset],
  );
  return run;
}

export interface Typed {
  reason: string;
  /** Typed for this act; never kept. */
  password: string;
}

export interface LessonAct extends Typed {
  id: number;
  verb: 'link' | 'close' | 'dismiss';
  fixKind?: 'hypothesis' | 'experiment';
  fixId?: number;
}

/** Link, close or dismiss one lesson. Close and dismiss send a reason and the password only: no number, no state. */
export function useLessonAct() {
  return useAct(async ({ id, verb, reason, password, fixKind, fixId }: LessonAct) => {
    const params = { path: { id } };
    if (verb === 'link') {
      return unwrap(
        await api.POST('/api/lessons/{id}/link', {
          params,
          body: { fixKind: fixKind ?? '', fixId: fixId ?? 0, reason, password },
        }),
      );
    }
    const body = { reason, password };
    return verb === 'close'
      ? unwrap(await api.POST('/api/lessons/{id}/close', { params, body }))
      : unwrap(await api.POST('/api/lessons/{id}/dismiss', { params, body }));
  });
}

export interface HypothesisAct extends Typed {
  id: number;
  to: string;
}

export function useHypothesisAct() {
  return useAct(async ({ id, to, reason, password }: HypothesisAct) =>
    unwrap(await api.POST('/api/hypotheses/{id}/move', { params: { path: { id } }, body: { to, reason, password } })),
  );
}

export interface RevertAct extends Typed {
  id: number;
}

export function useRevertAct() {
  return useAct(async ({ id, reason, password }: RevertAct) =>
    unwrap(await api.POST('/api/changelog/{id}/revert', { params: { path: { id } }, body: { reason, password } })),
  );
}

export interface AutonomyAct extends Typed {
  autoApproveTickets: boolean;
  promoteFromReserve: boolean;
  minPWin: number;
  maxAutoPerDay: number;
  /** The row the form was loaded at; 0 when nothing was stored. */
  expectedSettingsId: number;
}

export function useAutonomyAct() {
  return useAct(async (input: AutonomyAct) => unwrap(await api.PUT('/api/autonomy/settings', { body: input })));
}
