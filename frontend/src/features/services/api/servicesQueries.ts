import { keepPreviousData, queryOptions, skipToken } from '@tanstack/react-query';
import {
  api,
  queryRoots,
  servicesListQuery,
  servicesPortKeys,
  servicesTimelineQuery,
  unwrap,
  type ServiceRun,
  type ServiceRunPage,
  type ServiceScheduleInput,
} from '@/shared/api';
import type { InfiniteRowsOptions } from '@/shared/lib/useInfiniteRows';

/** Query keys: ['services', resource, …]. The notices of the bell are `serviceNoticesQuery` in shared/api. */
export const servicesKeys = {
  all: queryRoots.services,
  list: servicesPortKeys.list,
  timeline: servicesPortKeys.timeline,
  runs: ['services', 'runs'] as const,
  preview: ['services', 'preview'] as const,
};

/** Every service. Polling is added by the watcher. Shared with the Portfolio screen (see `servicesPort`). */
export const servicesQuery = servicesListQuery;

/** The trading-day timeline of a date (YYYY-MM-DD); left out, the server's today. */
export const timelineQuery = servicesTimelineQuery;

/**
 * The dry run of a save: the checks and the next three starts. Without an input (the draft is not complete) nothing is
 * asked. The revision is not part of the key (the server does not look at it).
 */
export const previewQuery = (id: string, input: ServiceScheduleInput | null) =>
  queryOptions({
    queryKey: [...servicesKeys.preview, id, input && { ...input, revision: 0 }] as const,
    queryFn:
      input === null
        ? skipToken
        : async ({ signal }) =>
            unwrap(
              await api.POST('/api/services/{id}/schedule/preview', { params: { path: { id } }, body: input, signal }),
            ),
    // The previous answer stays while the next one is on its way, so the dialog does not flicker while the owner types.
    placeholderData: keepPreviousData,
    staleTime: 30_000,
    gcTime: 60_000,
  });

const runId = (run: ServiceRun) => run.id;

/** The runs of a service, newest first: pages loaded as the owner scrolls (see `useInfiniteRows`). */
export const runsList = (id: string): InfiniteRowsOptions<ServiceRun, ServiceRunPage> => ({
  queryKey: [...servicesKeys.runs, id],
  getRowId: runId,
  fetchPage: async (page, { pageSize, signal }) =>
    unwrap(await api.GET('/api/services/{id}/runs', { params: { path: { id }, query: { page, pageSize } }, signal })),
});
