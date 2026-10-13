import { queryOptions } from '@tanstack/react-query';
import { api, unwrap, type PriceAlert, type PriceAlertPage } from '@/shared/api';
import type { InfiniteRowsOptions } from '@/shared/lib/useInfiniteRows';
import type { AlertStatus } from '../lib/alerts';
import { portfolioKeys } from './portfolioQueries';

/** Alerts are listed 50 at a time ("Show more" asks for the next page). */
export const alertsPageSize = 50;

/** The alerts of one status, newest first, as pages that grow with "Show more". */
export const alertsList = (status: AlertStatus): InfiniteRowsOptions<PriceAlert, PriceAlertPage> => ({
  queryKey: [...portfolioKeys.alerts, 'list', status],
  pageSize: alertsPageSize,
  getRowId: (alert) => alert.id,
  // Each status is a different list: the rows of another tab are never shown under this one's name.
  keepPreviousRows: false,
  fetchPage: async (page, { pageSize, signal }) =>
    unwrap(await api.GET('/api/portfolio/alerts', { params: { query: { status, page, pageSize } }, signal })),
});

/** How many alerts a status has (one row is asked for: the page carries the total, and the active count and limit). */
export const alertCountQuery = (status: AlertStatus) =>
  queryOptions({
    queryKey: [...portfolioKeys.alerts, 'count', status] as const,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/portfolio/alerts', { params: { query: { status, page: 1, pageSize: 1 } }, signal })),
  });
