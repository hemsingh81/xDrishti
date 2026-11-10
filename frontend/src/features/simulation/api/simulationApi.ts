import { queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';
import { api, ApiError, unwrap, type Schemas } from '@/shared/api';

export type SimJob = Schemas['SimAllView'];
export type SimAction = 'start' | 'pause' | 'resume' | 'cancel';

export const simJobKey = ['simulation', 'job'] as const;
const noJob = 'simulate_all.no_active_job';

/** True while the job still moves; the progress is read often, the report is not (the owner refreshes it). */
export const isActive = (job: SimJob | null | undefined) => job?.state === 'running' || job?.state === 'paused';

/** The active job, else the newest; `null` when there never was one (404 simulate_all.no_active_job). */
export const simJobQuery = queryOptions({
  queryKey: simJobKey,
  queryFn: async ({ signal }): Promise<SimJob | null> => {
    try {
      return unwrap(await api.GET('/api/simulate-all/current', { signal }));
    } catch (error) {
      if (error instanceof ApiError && error.problem?.code === noJob) return null;
      throw error;
    }
  },
  refetchInterval: (query) => (isActive(query.state.data) ? 5_000 : false),
  retry: false,
});

const send = async (action: SimAction) => {
  switch (action) {
    case 'start':
      return unwrap(await api.POST('/api/simulate-all'));
    case 'pause':
      return unwrap(await api.POST('/api/simulate-all/pause'));
    case 'resume':
      return unwrap(await api.POST('/api/simulate-all/resume'));
    case 'cancel':
      return unwrap(await api.POST('/api/simulate-all/cancel'));
  }
};

/** Start, pause, resume or cancel the job. A refusal (409, 422) shows its message; the job is read again either way. */
export function useSimAction() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: send,
    onSuccess: (job) => {
      queryClient.setQueryData(simJobKey, job);
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: simJobKey }),
  });
}
