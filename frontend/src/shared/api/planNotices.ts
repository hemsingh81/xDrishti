import { queryOptions } from '@tanstack/react-query';
import { api, unwrap } from './client';

/** How often the bell looks for plan notices (a plan to review, a nightly run that failed closed, tickets armed or lapsed); also on window focus. */
export const planNoticesPollMs = 60_000;

/**
 * What the owner should know about the nightly plan, for the notification bell (read only, derived by the API from the stored
 * plan record; an item is gone once its cause is). It lives here, not in the plans feature, because the bell (a lazy chunk of the
 * shell) reads it and must not pull the Plans screen in; the Plans screen invalidates it by key (`['plans', 'notices']`) after
 * a decision.
 */
export const planNoticesQuery = queryOptions({
  queryKey: ['plans', 'notices'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/plans/notices', { signal })),
  refetchInterval: planNoticesPollMs,
  refetchOnWindowFocus: true,
});
