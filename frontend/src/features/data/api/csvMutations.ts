import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api';
import { dataKeys } from './dataQueries';
import { reloadAfterStart, showStartedJob } from './startMutation';

/**
 * Undoes the load of one CSV file as a data job (202, the job): the page's watcher follows it like any other job and
 * reloads the bars when it ends. A 404 means the file was never loaded, a 409 that a job is running.
 */
export function useUndoCsvLoad() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (name: string) =>
      unwrap(await api.POST('/api/data/csv/files/{name}/undo', { params: { path: { name } } })),
    onSuccess: (job) => showStartedJob(queryClient, job),
    onSettled: () =>
      Promise.all([reloadAfterStart(queryClient), queryClient.invalidateQueries({ queryKey: dataKeys.csvFiles })]),
  });
}
