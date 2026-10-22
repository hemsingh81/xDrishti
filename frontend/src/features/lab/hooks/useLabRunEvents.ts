import { useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { useEventsHub } from '@/shared/realtime';
import { runKeys, type LabRun } from '../api/runQueries';

/**
 * Follows the Lab runs over the hub while `enabled` (a run is going): a progress event is written into the cached run, the
 * end event reads the run, the lists, the report and the ledger again. Returns whether the hub carries the events; when it
 * does not, the caller polls `GET /api/lab/runs/{id}` instead.
 */
export function useLabRunEvents(enabled: boolean): { live: boolean } {
  const queryClient = useQueryClient();
  const { hub, state } = useEventsHub(enabled);

  useEffect(() => {
    const offProgress = hub.onLabProgress((m) => {
      queryClient.setQueryData<LabRun>(runKeys.one(m.runId), (run) =>
        run ? { ...run, state: m.state, stage: m.stage, progress: { done: m.done, total: m.total } } : run,
      );
      queryClient.setQueriesData<LabRun[]>({ queryKey: ['lab', 'runs', 'list'] }, (runs) =>
        runs?.map((r) =>
          r.id === m.runId ? { ...r, state: m.state, stage: m.stage, progress: { done: m.done, total: m.total } } : r,
        ),
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

  return { live: enabled && state.connection === 'connected' };
}
