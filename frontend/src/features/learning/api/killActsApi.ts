import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api';

// CF-12: the owner's acts on kill proposals. These are the only writes of the Learning feature. Nothing here runs on load;
// each is one explicit, confirmed call, and none places an order.

export type KillAct =
  | { kind: 'dismiss'; id: number; reason: string; password: string }
  | { kind: 'apply'; id: number }
  | { kind: 'reenable'; key: string; reason: string; password: string }
  | { kind: 'auto'; auto: boolean; reason: string; password: string };

/** What a finished act says, in words the screen prints. Built from the server's answer, never from the request. */
async function run(act: KillAct): Promise<string> {
  switch (act.kind) {
    case 'dismiss': {
      const out = unwrap(
        await api.POST('/api/confidence/kill-proposals/{id}/dismiss', {
          params: { path: { id: act.id } },
          body: { reason: act.reason, password: act.password },
        }),
      );
      return out.alreadyDismissed
        ? `Already dismissed by ${out.actor ?? 'not stated'} (reason: ${out.reason ?? 'not stated'}); this call recorded nothing new.`
        : `Proposal ${String(out.proposalId)} dismissed. No cell was changed.`;
    }
    case 'apply': {
      const out = unwrap(
        await api.POST('/api/confidence/kill-proposals/{id}/apply', { params: { path: { id: act.id } } }),
      );
      const paused = out.pausedCells.length === 0 ? 'none' : out.pausedCells.join(', ');
      return out.alreadyApplied
        ? 'Already applied earlier; this call paused no further cell.'
        : `Proposal ${String(out.proposalId)} applied. Paused: ${paused}. Skipped: ${out.skippedCells.join(', ') || 'none'}.`;
    }
    case 'reenable': {
      const out = unwrap(
        await api.POST('/api/learning/cells/{key}/re-enable', {
          params: { path: { key: act.key } },
          body: { reason: act.reason, password: act.password },
        }),
      );
      return [`Cell ${out.cell} moved from ${out.from} to ${out.to}.`, ...out.warnings].join(' ');
    }
    case 'auto': {
      const out = unwrap(
        await api.PUT('/api/confidence/kill-settings', {
          body: { auto: act.auto, reason: act.reason, password: act.password },
        }),
      );
      return `Automatic kill is now ${out.auto ? 'ON' : 'OFF'}.`;
    }
  }
}

/** Runs one act. The typed password does not stay in the mutation cache; whatever the answer, the Learning reads are refetched. */
export function useKillAct() {
  const queryClient = useQueryClient();
  return useMutation({
    gcTime: 0,
    mutationFn: run,
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['learning'] }),
  });
}
