import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useEffectEvent, useRef } from 'react';
import { queryRoots, type StartDataJobInput } from '@/shared/api';
import { useToast } from '@/shared/ui/toastContext';
import { dataKeys, jobQuery, overviewQuery } from '../api/dataQueries';
import { useStartDataJob } from '../api/startMutation';
import { outcomeText } from '../lib/labels';
import { dataPollMs } from '../lib/polling';

/**
 * The overview plus the "start" action. The overview carries the job in progress, so it is read every couple of
 * seconds while one runs (also one that was already running when the page opened) and not otherwise. When the job is
 * gone from it, the table, the runs and the bars are reloaded and a toast tells the owner how it ended.
 */
export function useDataWatch() {
  const queryClient = useQueryClient();
  const toast = useToast();
  const start = useStartDataJob();

  const overview = useQuery({
    ...overviewQuery,
    refetchInterval: (query) => (query.state.data?.runningJob ? dataPollMs : false),
  });

  const finished = useEffectEvent(async (id: string) => {
    start.reset();
    // Everything that depends on the stored bars; a CSV load also tracks the instruments it found.
    for (const queryKey of [
      dataKeys.coverage,
      dataKeys.bars,
      dataKeys.jobs,
      queryRoots.instruments,
      queryRoots.basketMembers,
    ]) {
      void queryClient.invalidateQueries({ queryKey });
    }
    try {
      const job = await queryClient.query({ ...jobQuery(id), staleTime: 0 });
      toast.notify(outcomeText(job), job.status === 'failed' ? 'error' : 'success');
    } catch {
      toast.notify('The update finished.', 'info');
    }
  });

  // The job that was in progress at the last look is not any more: it ended.
  const runningId = overview.data ? (overview.data.runningJob?.id ?? null) : undefined;
  const seenId = useRef<string | null | undefined>(undefined);
  useEffect(() => {
    if (runningId === undefined) return;
    const previous = seenId.current;
    seenId.current = runningId;
    if (previous && previous !== runningId) void finished(previous);
  }, [runningId]);

  const running = overview.data?.runningJob ?? null;
  return {
    overview,
    running,
    /** True from the click until the job is over. */
    busy: running !== null || start.isPending,
    startJob: (body: StartDataJobInput) => {
      start.mutate(body);
    },
    startError: start.error,
    dismissStartError: start.reset,
  };
}
