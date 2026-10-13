import { queryOptions } from '@tanstack/react-query';
import { api, unwrap } from './client';

/** How often the bell looks for Portfolio notices (a triggered price alert, items to decide); also on window focus. */
export const portfolioNoticesPollMs = 60_000;

/**
 * What the Portfolio module wants the owner to know, for the notification bell. It lives here, not in the portfolio
 * feature, because the bell (a lazy chunk of the shell) reads it and must not pull the Portfolio screen in; the Portfolio
 * screen invalidates it by key (`['portfolio', 'notices']`) when an alert changes or a decision is made.
 */
export const portfolioNoticesQuery = queryOptions({
  queryKey: ['portfolio', 'notices'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/portfolio/notices', { signal })),
  refetchInterval: portfolioNoticesPollMs,
  refetchOnWindowFocus: true,
});
