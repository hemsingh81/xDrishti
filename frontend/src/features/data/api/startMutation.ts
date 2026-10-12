import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { api, unwrap, type DataJob, type StartDataJobInput } from '@/shared/api';
import { dataKeys, overviewQuery } from './dataQueries';

/** Shows a job that was just accepted at once: a read that started before the click must not bring back "nothing is running". */
export async function showStartedJob(queryClient: QueryClient, job: DataJob) {
  await queryClient.cancelQueries({ queryKey: dataKeys.overview });
  if (job.status === 'running') {
    queryClient.setQueryData(overviewQuery.queryKey, (overview) =>
      overview ? { ...overview, runningJob: job } : overview,
    );
  }
}

/** After any attempt to start a job (also a refused one): the overview and the runs say what is going on. */
export function reloadAfterStart(queryClient: QueryClient) {
  return Promise.all([
    queryClient.invalidateQueries({ queryKey: dataKeys.overview }),
    queryClient.invalidateQueries({ queryKey: dataKeys.jobs }),
  ]);
}

/** Starts a data job (202, the job). A 409 `data.job_running` means one is already running; callers just watch it. */
export function useStartDataJob() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: StartDataJobInput) => unwrap(await api.POST('/api/data/refresh', { body })),
    onSuccess: (job) => showStartedJob(queryClient, job),
    onSettled: () => reloadAfterStart(queryClient),
  });
}
