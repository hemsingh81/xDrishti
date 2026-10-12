import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api, dataOverviewQuery, unwrap } from '@/shared/api';

/** What to start: the checks over every instrument (or one), or a fix action on one instrument. */
export type QualityJobRequest =
  | { kind: 'run'; instrumentId?: string }
  | { kind: 'refetch'; instrumentId: string }
  | { kind: 'refresh'; instrumentId: string }
  /** The fix for everything: fetch the missing bars from Dhan (when it can be asked), rebuild the aggregates, check again. */
  | { kind: 'fixAll'; dhan: boolean };

async function start(request: QualityJobRequest) {
  switch (request.kind) {
    case 'run':
      return unwrap(
        await api.POST('/api/data/quality/run', {
          body: request.instrumentId ? { instrumentId: request.instrumentId } : {},
        }),
      );
    case 'refetch':
      return unwrap(
        await api.POST('/api/data/quality/instruments/{id}/refetch', {
          params: { path: { id: request.instrumentId } },
        }),
      );
    case 'fixAll':
      return unwrap(
        await api.POST('/api/data/refresh', {
          body: request.dhan
            ? { steps: ['dhan', 'aggregate', 'quality'], refetchGaps: true }
            : { steps: ['aggregate', 'quality'] },
        }),
      );
    case 'refresh':
      return unwrap(
        await api.POST('/api/data/quality/instruments/{id}/refresh-aggregates', {
          params: { path: { id: request.instrumentId } },
        }),
      );
  }
}

/**
 * Starts a quality job (202, a data job). A 409 `data.job_running` means one is already running; the watcher shows it.
 * The job is put in the data overview at once, so a read that started before the click cannot bring back "nothing is
 * running".
 */
export function useStartQualityJob() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: start,
    onSuccess: async (job) => {
      await queryClient.cancelQueries({ queryKey: dataOverviewQuery.queryKey });
      if (job.status === 'running') {
        queryClient.setQueryData(dataOverviewQuery.queryKey, (overview) =>
          overview ? { ...overview, runningJob: job } : overview,
        );
      }
    },
    onSettled: async () => {
      await queryClient.invalidateQueries({ queryKey: dataOverviewQuery.queryKey });
    },
  });
}
