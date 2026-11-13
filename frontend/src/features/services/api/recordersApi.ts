import { queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type RecorderView = Schemas['OptionChainRecorderView'];
export type RecorderChange = Schemas['OptionChainSwitchRequest'];

// The card reads all recorders through one shape: this fails to compile if the generated shapes drift apart.
type Same<A, B> = [A] extends [B] ? ([B] extends [A] ? true : never) : never;
export const recorderShapesMatch: [
  Same<RecorderChange, Schemas['OptionBackfillSwitchRequest']>,
  Same<RecorderView, Schemas['OpeningBarsRecorderView']>,
  Same<RecorderChange, Schemas['OpeningBarsSwitchRequest']>,
] = [true, true, true];

export type RecorderKind = 'chain' | 'backfill' | 'opening';

/** What any recorder card reads: the common switch shape, plus the progress and probe acceptance for the backfill. */
export type AnyRecorderView = RecorderView | Schemas['OptionBackfillView'];

export const recorderKeys = {
  all: ['services', 'recorders'] as const,
  one: (kind: RecorderKind) => ['services', 'recorders', kind] as const,
};

/** The owner's switch of a recorder, its history and why it is not running. Contains no market data. */
export const recorderQuery = (kind: RecorderKind) =>
  queryOptions({
    queryKey: recorderKeys.one(kind),
    queryFn: async ({ signal }): Promise<AnyRecorderView> => {
      switch (kind) {
        case 'chain':
          return unwrap(await api.GET('/api/options/chain-recorder', { signal }));
        case 'backfill':
          return unwrap(await api.GET('/api/options/backfill', { signal }));
        case 'opening':
          return unwrap(await api.GET('/api/opening-bars/recorder', { signal }));
      }
    },
    staleTime: 15_000,
  });

/** Switches a recorder ON or OFF; the password is checked afresh by the server on every change. */
export function useChangeRecorder(kind: RecorderKind) {
  const queryClient = useQueryClient();
  return useMutation({
    // The typed password does not stay in the mutation cache once nothing watches the mutation.
    gcTime: 0,
    mutationFn: async (body: RecorderChange) => {
      switch (kind) {
        case 'chain':
          return unwrap(await api.PUT('/api/options/chain-recorder', { body }));
        case 'backfill':
          return unwrap(await api.PUT('/api/options/backfill', { body }));
        case 'opening':
          return unwrap(await api.PUT('/api/opening-bars/recorder', { body }));
      }
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: recorderKeys.one(kind) }),
  });
}
