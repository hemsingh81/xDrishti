import { queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type Preview = Schemas['ExperimentPreviewView'];
export type Job = Schemas['ExperimentJobView'];
export type Judgement = Schemas['ExperimentView'];
export type RunItem = Schemas['RunListItemView'];

export const experimentKeys = {
  runs: ['owner-acts', 'runs'] as const,
  preview: (runKey: string) => ['owner-acts', 'preview', runKey] as const,
  job: (id: string) => ['owner-acts', 'job', id] as const,
  judgement: (id: number) => ['owner-acts', 'judgement', id] as const,
};

/** The states after which a job never moves again. */
export const isFinal = (state: string): boolean => state !== 'queued' && state !== 'running';

export const runsQuery = () =>
  queryOptions({
    queryKey: experimentKeys.runs,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/reports/runs', { signal })),
  });

export const previewQuery = (runKey: string) =>
  queryOptions({
    queryKey: experimentKeys.preview(runKey),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/experiments/preview', { params: { query: { baselineRunKey: runKey } }, signal })),
    // A look is a count that moves with every start: never serve an old one.
    staleTime: 0,
  });

export const jobQuery = (id: string, pollMs: number | false) =>
  queryOptions({
    queryKey: experimentKeys.job(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/experiments/jobs/{id}', { params: { path: { id } }, signal })),
    refetchInterval: (query) => (query.state.data && isFinal(query.state.data.state) ? false : pollMs),
  });

export const judgementQuery = (id: number) =>
  queryOptions({
    queryKey: experimentKeys.judgement(id),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/experiments/{id}', { params: { path: { id } }, signal })),
  });

export interface StartInput {
  baselineRunKey: string;
  kind: 'exit' | 'slippage';
  name: string;
  candidateValue: number;
  reason: string;
  /** Typed for this start; never kept. */
  password: string;
}

/** Starts one replay. The answer is the job (a new one, or the same input's existing one). */
export function useStartExperiment() {
  const queryClient = useQueryClient();
  return useMutation({
    gcTime: 0, // the password is in the variables of this mutation: it is not kept after the call
    mutationFn: async ({ baselineRunKey, kind, name, candidateValue, reason, password }: StartInput) =>
      unwrap(
        await api.POST('/api/experiments', {
          // `holdOutMonths` is optional on the wire (the server keeps 6); the generated type lists it as required.
          body: {
            baselineRunKey,
            knob: { kind, name, candidateValue },
            reason,
            password,
          } as Schemas['StartExperimentRequest'],
        }),
      ),
    onSettled: (_job, _error, input) => {
      void queryClient.invalidateQueries({ queryKey: experimentKeys.preview(input.baselineRunKey) });
    },
  });
}

/** Stops a queued or running job. No password: stopping is the safe direction. */
export function useCancelJob() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) =>
      unwrap(await api.POST('/api/experiments/jobs/{id}/cancel', { params: { path: { id } } })),
    onSuccess: (job) => {
      queryClient.setQueryData(experimentKeys.job(job.id), job);
    },
  });
}
