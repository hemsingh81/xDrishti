import { useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { useEventsHub } from '@/shared/realtime';
import { experimentKeys, isFinal, type Job } from '../api/experimentApi';

/**
 * Follows a job over the hub: a progress event is written into the cached job (unless it is already over: an older event
 * must not revive it), the end event reads the job again. Returns whether the hub carries the events; the caller polls
 * every 2 s only when it does not.
 */
export function useExperimentEvents(jobId: string | undefined): { live: boolean } {
  const queryClient = useQueryClient();
  const { hub, state } = useEventsHub(jobId !== undefined);
  const connected = state.connection === 'connected';

  useEffect(() => {
    if (!jobId) return;
    const offProgress = hub.onExperimentProgress((m) => {
      if (m.jobId !== jobId) return;
      queryClient.setQueryData<Job>(experimentKeys.job(jobId), (job) =>
        job && !isFinal(job.state) ? { ...job, state: m.state, stage: m.stage, done: m.done, total: m.total } : job,
      );
    });
    const offFinished = hub.onExperimentFinished((m) => {
      if (m.jobId === jobId) void queryClient.invalidateQueries({ queryKey: experimentKeys.job(jobId) });
    });
    return () => {
      offProgress();
      offFinished();
    };
  }, [hub, queryClient, jobId]);

  useEffect(() => {
    // Events may have been missed while the connection was down.
    if (connected && jobId) void queryClient.invalidateQueries({ queryKey: experimentKeys.job(jobId) });
  }, [connected, queryClient, jobId, state.connects]);

  return { live: connected };
}
