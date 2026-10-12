import { queryOptions } from '@tanstack/react-query';
import { api, unwrap } from './client';

/** How often the bell looks for data notices (a failed scheduled run, open gaps); also on window focus. */
export const dataNoticesPollMs = 60_000;

/**
 * What the owner should know about the stored data, for the notification bell. It lives here, not in the data feature,
 * because the bell (a lazy chunk of the shell) reads it and must not pull the Data screen in; the Data screen
 * invalidates it by key when a run ends.
 */
export const dataNoticesQuery = queryOptions({
  queryKey: ['data', 'notices'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/data/notices', { signal })),
  refetchInterval: dataNoticesPollMs,
  refetchOnWindowFocus: true,
});
