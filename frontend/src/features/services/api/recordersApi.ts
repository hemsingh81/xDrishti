import { queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type RecorderView = Schemas['DepthRecorderView'];
export type RecorderChange = Schemas['DepthSwitchRequest'];

// The card reads both recorders through one shape: this fails to compile if the two generated shapes drift apart.
type Same<A, B> = [A] extends [B] ? ([B] extends [A] ? true : never) : never;
export const recorderShapesMatch: [
  Same<RecorderView, Schemas['TickRecorderView']>,
  Same<RecorderChange, Schemas['TickSwitchRequest']>,
] = [true, true];

export type RecorderKind = 'depth' | 'ticks';

export const recorderKeys = {
  all: ['services', 'recorders'] as const,
  one: (kind: RecorderKind) => ['services', 'recorders', kind] as const,
};

/** The owner's switch of a recorder, its history and why it is not running. Contains no market data. */
export const recorderQuery = (kind: RecorderKind) =>
  queryOptions({
    queryKey: recorderKeys.one(kind),
    queryFn: async ({ signal }): Promise<RecorderView> =>
      kind === 'depth'
        ? unwrap(await api.GET('/api/depth/recorder', { signal }))
        : unwrap(await api.GET('/api/ticks/recorder', { signal })),
    staleTime: 15_000,
  });

/** Switches a recorder ON or OFF; the password is checked afresh by the server on every change. */
export function useChangeRecorder(kind: RecorderKind) {
  const queryClient = useQueryClient();
  return useMutation({
    // The typed password does not stay in the mutation cache once nothing watches the mutation.
    gcTime: 0,
    mutationFn: async (body: RecorderChange) =>
      kind === 'depth'
        ? unwrap(await api.PUT('/api/depth/recorder', { body }))
        : unwrap(await api.PUT('/api/ticks/recorder', { body })),
    onSettled: () => queryClient.invalidateQueries({ queryKey: recorderKeys.one(kind) }),
  });
}
