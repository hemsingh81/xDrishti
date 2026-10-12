import { queryOptions } from '@tanstack/react-query';
import { api, unwrap } from './client';

/**
 * The data overview (it carries the job in progress) and one data job. They live here, not in the data feature, because
 * more than one screen watches a job: the Data & import screen and the Data quality screen (a quality run is a data
 * job). The keys are the data feature's own, so both screens share one cache; polling is added by the watcher.
 */
export const dataOverviewQuery = queryOptions({
  queryKey: ['data', 'overview'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/data/overview', { signal })),
});

export const dataJobQuery = (id: string) =>
  queryOptions({
    queryKey: ['data', 'job', id] as const,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/data/jobs/{id}', { params: { path: { id } }, signal })),
  });

/**
 * While no job runs, the overview is read this often (and only while the page is visible), so that an automatic run
 * that starts on its own is noticed on a page that was left open. While a job runs the watcher reads it faster.
 */
export const dataIdlePollMs = 60_000;
