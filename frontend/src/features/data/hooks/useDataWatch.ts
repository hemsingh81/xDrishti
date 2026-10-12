import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useEffectEvent, useRef } from 'react';
import { dataIdlePollMs, queryRoots, type StartDataJobInput } from '@/shared/api';
import { useRefetchAt } from '@/shared/lib/useRefetchAt';
import { useToast } from '@/shared/ui/toastContext';
import { jobQuery, overviewQuery } from '../api/dataQueries';
import { useStartDataJob } from '../api/startMutation';
import { outcomeText } from '../lib/labels';
import { dataPollMs } from '../lib/polling';

/**
 * The overview plus the "start" action. The overview carries the job in progress, so it is read every couple of
 * seconds while one runs (also one that was already running when the page opened). Otherwise it is read once a minute
 * while the page is visible, and once just after the next automatic run is due (`eod.nextRunAt`), so that an open page
 * notices a run that started by itself. When the job is gone from it, everything on the screen (the table, the runs,
 * the bars, the calendar and the corporate actions the job may have proposed) is reloaded and a toast tells the owner
 * how it ended.
 */
export function useDataWatch() {
  const queryClient = useQueryClient();
  const toast = useToast();
  const start = useStartDataJob();

  const overview = useQuery({
    ...overviewQuery,
    // Background tabs are skipped (refetchIntervalInBackground is off): the page catches up when it is shown again.
    refetchInterval: (query) => (query.state.data?.runningJob ? dataPollMs : dataIdlePollMs),
  });

  const finished = useEffectEvent(async (id: string) => {
    start.reset();
    // Everything on the screen (a run may also have proposed calendar days and corporate actions); a CSV load also
    // tracks the instruments it found.
    for (const queryKey of [queryRoots.data, queryRoots.quality, queryRoots.instruments, queryRoots.basketMembers]) {
      void queryClient.invalidateQueries({ queryKey });
    }
    try {
      const job = await queryClient.query({ ...jobQuery(id), staleTime: 0 });
      toast.notify(outcomeText(job), job.status === 'failed' ? 'error' : job.status === 'skipped' ? 'info' : 'success');
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

  // The automatic run is due: read the overview again, it carries the job when it started.
  useRefetchAt(overview.data?.eod.nextRunAt, () => {
    void overview.refetch();
  });

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
