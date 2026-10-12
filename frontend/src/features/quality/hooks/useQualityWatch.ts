import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useEffectEvent, useRef } from 'react';
import { dataIdlePollMs, dataJobQuery, dataOverviewQuery, queryRoots } from '@/shared/api';
import { useToast } from '@/shared/ui/toastContext';
import { useStartQualityJob, type QualityJobRequest } from '../api/startMutation';
import { qualityKeys } from '../api/qualityQueries';
import { qualityPollMs } from '../lib/polling';
import { outcomeText } from '../lib/progress';

/**
 * The job in progress plus the "start" actions. A quality run is a data job, so the data overview carries it; that
 * overview is read every couple of seconds while a job runs (also one that was already running when the page opened) and
 * once a minute while the page is visible otherwise. When the job is gone from it, everything that depends on the checks and the bars is reloaded and a toast
 * tells the owner how it ended. The Dhan state of the overview tells whether a re-fetch can be asked for.
 */
export function useQualityWatch() {
  const queryClient = useQueryClient();
  const toast = useToast();
  const start = useStartQualityJob();

  const overview = useQuery({
    ...dataOverviewQuery,
    // Once a minute while idle (not in a background tab), so that an automatic run is noticed on an open page.
    refetchInterval: (query) => (query.state.data?.runningJob ? qualityPollMs : dataIdlePollMs),
  });

  const finished = useEffectEvent(async (id: string) => {
    start.reset();
    // The checks, and the stored bars, gaps and runs a fix action may have changed.
    for (const queryKey of [qualityKeys.all, queryRoots.data]) {
      void queryClient.invalidateQueries({ queryKey });
    }
    try {
      const job = await queryClient.query({ ...dataJobQuery(id), staleTime: 0 });
      toast.notify(outcomeText(job), job.status === 'failed' ? 'error' : job.status === 'skipped' ? 'info' : 'success');
    } catch {
      toast.notify('The job finished.', 'info');
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
    /** The data overview: the job in progress and whether Dhan can be asked. */
    overview,
    running,
    dhan: overview.data?.dhan ?? null,
    /** True from the click until the job is over. */
    busy: running !== null || start.isPending,
    startJob: (request: QualityJobRequest) => {
      start.mutate(request);
    },
    startError: start.error,
    dismissStartError: start.reset,
  };
}

export type QualityWatch = ReturnType<typeof useQualityWatch>;
