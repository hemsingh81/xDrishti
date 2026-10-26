import { queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';
import { api, queryRoots, unwrap } from '@/shared/api';

export const chargeSheetKey = ['charge-sheet', 'confirmation'] as const;

/** What stands about the owner's confirmation, and a preview of the sheet named (else the one in force). */
export const chargeSheetQuery = (sheetId: string) =>
  queryOptions({
    queryKey: [...chargeSheetKey, sheetId],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/charge-sheet/confirmation', { params: { query: sheetId ? { sheetId } : {} }, signal }),
      ),
  });

/** One owner act. The password is for this act only; the server checks it every time. */
export type ChargeSheetAct =
  | { kind: 'confirm'; sheetId: string; ratesSha256: string; reason: string; password: string }
  | { kind: 'revoke'; reason: string; password: string }
  | { kind: 'capability'; on: boolean; reason: string; password: string };

async function send(act: ChargeSheetAct) {
  switch (act.kind) {
    case 'confirm':
      return unwrap(
        await api.POST('/api/charge-sheet/confirmation', {
          body: { sheetId: act.sheetId, ratesSha256: act.ratesSha256, reason: act.reason, password: act.password },
        }),
      );
    case 'revoke':
      return unwrap(
        await api.POST('/api/charge-sheet/confirmation/revoke', {
          body: { reason: act.reason, password: act.password },
        }),
      );
    case 'capability':
      return unwrap(
        await api.PUT('/api/charge-sheet/capability', {
          body: { on: act.on, reason: act.reason, password: act.password },
        }),
      );
  }
}

/**
 * Runs an act. The answer is the new status, which replaces the cached view of the sheet on show; every other
 * status view, and the Reports and Paper screens (reached by their shared roots) whose net figures depend on the
 * confirmation, are read again.
 */
export function useChargeSheetAct(shown: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: send,
    onSuccess: ({ status }) => {
      queryClient.setQueryData([...chargeSheetKey, shown], status);
      void queryClient.invalidateQueries({ queryKey: chargeSheetKey, refetchType: 'none' });
      void queryClient.invalidateQueries({ queryKey: queryRoots.reports });
      void queryClient.invalidateQueries({ queryKey: queryRoots.paper });
    },
  });
}
