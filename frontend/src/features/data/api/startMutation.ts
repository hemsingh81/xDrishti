import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api, unwrap, type StartDataJobInput } from '@/shared/api';
import { dataKeys, overviewQuery } from './dataQueries';

/** Starts a data job (202, the job). A 409 `data.job_running` means one is already running; callers just watch it. */
export function useStartDataJob() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: StartDataJobInput) => unwrap(await api.POST('/api/data/refresh', { body })),
    onSuccess: async (job) => {
      // Show the job at once: a read that started before the click must not bring back "nothing is running".
      await queryClient.cancelQueries({ queryKey: dataKeys.overview });
      if (job.status === 'running') {
        queryClient.setQueryData(overviewQuery.queryKey, (overview) =>
          overview ? { ...overview, runningJob: job } : overview,
        );
      }
    },
    onSettled: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: dataKeys.overview }),
        queryClient.invalidateQueries({ queryKey: dataKeys.jobs }),
      ]);
    },
  });
}
