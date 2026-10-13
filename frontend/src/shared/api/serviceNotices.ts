import { queryOptions } from '@tanstack/react-query';
import { api, unwrap } from './client';

/** How often the bell looks for service notices (a failed run of a service that notifies on failure); also on window focus. */
export const serviceNoticesPollMs = 60_000;

/**
 * The failures of the services the owner should know about, for the notification bell. It lives here, not in the
 * services feature, because the bell (a lazy chunk of the shell) reads it and must not pull the Services screen in; the
 * Services screen invalidates it by key (`['services', 'notices']`) when a run ends or a service changes.
 */
export const serviceNoticesQuery = queryOptions({
  queryKey: ['services', 'notices'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/services/notices', { signal })),
  refetchInterval: serviceNoticesPollMs,
  refetchOnWindowFocus: true,
});
