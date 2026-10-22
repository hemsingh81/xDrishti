import { useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { useEventsHub } from '@/shared/realtime';
import type { LabRunProgress } from '@/shared/realtime';
import { isActive, runKeys, type LabRun } from '../api/runQueries';

/** Writes a progress event into a run, unless the cached run is already over (an older event must not revive it). */
export function applyProgress<T extends Pick<LabRun, 'state' | 'stage' | 'progress'>>(run: T, m: LabRunProgress): T {
  if (!isActive(run.state)) return run;
  return { ...run, state: m.state, stage: m.stage, progress: { done: m.done, total: m.total } };
}

/**
 * Follows the Lab runs over the hub while the Lab is open: a progress event is written into the cached run, the end event
 * reads the run, the lists, the report and the ledger again, and a (re)connect reads the runs again, as events may have
 * been missed. Returns whether the hub carries the events; the caller still polls, slowly when it does and fast when not.
 */
export function useLabRunEvents(): { live: boolean } {
  const queryClient = useQueryClient();
  const { hub, state } = useEventsHub(true);
  const connected = state.connection === 'connected';

  useEffect(() => {
    const offProgress = hub.onLabProgress((m) => {
      queryClient.setQueryData<LabRun>(runKeys.one(m.runId), (run) => (run ? applyProgress(run, m) : run));
      queryClient.setQueriesData<LabRun[]>({ queryKey: runKeys.lists }, (runs) =>
        runs?.map((r) => (r.id === m.runId ? applyProgress(r, m) : r)),
      );
    });
    const offFinished = hub.onLabFinished(() => {
      void queryClient.invalidateQueries({ queryKey: runKeys.all });
      void queryClient.invalidateQueries({ queryKey: ['lab', 'ledger'] });
    });
    return () => {
      offProgress();
      offFinished();
    };
  }, [hub, queryClient]);

  useEffect(() => {
    if (connected) void queryClient.invalidateQueries({ queryKey: runKeys.all });
  }, [connected, queryClient, state.connects]);

  return { live: connected };
}
