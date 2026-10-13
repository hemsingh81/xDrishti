import { queryOptions, type QueryClient } from '@tanstack/react-query';
import { api, unwrap } from './client';
import { queryRoots } from './queryRoots';
import type { ServiceList, ServiceRun } from './types';

/**
 * The part of the Services API that other screens use too (the Portfolio screen reads the quote service and runs it).
 * It lives here, not in the services feature, so a feature never imports another one; the Services screen builds on the
 * same query keys, so both share one cache.
 */
export const servicesPortKeys = {
  list: [...queryRoots.services, 'list'] as const,
  timeline: [...queryRoots.services, 'timeline'] as const,
};

/** Every service: the registered ones in screen order, then the read-only entries. Polling is added by the reader. */
export const servicesListQuery = queryOptions({
  queryKey: servicesPortKeys.list,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/services', { signal })),
});

/**
 * The trading-day timeline of a date (YYYY-MM-DD); left out, the server's today. `istDay` is the calendar day in India
 * now: it is part of the key of "today", so a page left open past midnight reads the new day's timeline.
 */
export const servicesTimelineQuery = (date?: string, istDay?: string) =>
  queryOptions({
    queryKey: [...servicesPortKeys.timeline, date ?? 'today', istDay ?? ''] as const,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/services/timeline', { params: { query: date ? { date } : {} }, signal })),
  });

/** Run now (202, the run, which goes on in the background). 409 `services.already_running` when it runs. */
export async function runServiceNow(id: string): Promise<ServiceRun> {
  return unwrap(await api.POST('/api/services/{id}/run', { params: { path: { id } } }));
}

/** Shows a run that was just accepted at once: a read that started before the click must not bring back "not running". */
export async function showStartedRun(queryClient: QueryClient, run: ServiceRun) {
  await queryClient.cancelQueries({ queryKey: servicesPortKeys.list });
  if (run.status !== 'running') return;
  queryClient.setQueryData<ServiceList>(servicesPortKeys.list, (list) =>
    list
      ? {
          ...list,
          items: list.items.map((item) =>
            item.id === run.serviceId ? { ...item, status: 'running', lastRun: run } : item,
          ),
        }
      : list,
  );
}
