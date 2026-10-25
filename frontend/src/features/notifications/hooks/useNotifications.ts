import { useQuery } from '@tanstack/react-query';
import { useMemo } from 'react';
import { accountsQuery, useNow } from '@/features/accounts';
import { dataNoticesQuery, planNoticesQuery, portfolioNoticesQuery, serviceNoticesQuery } from '@/shared/api';
import { deriveDataNotifications } from '../lib/deriveDataNotifications';
import { derivePlanNotifications } from '../lib/derivePlanNotifications';
import { derivePortfolioNotifications } from '../lib/derivePortfolioNotifications';
import { deriveServiceNotifications } from '../lib/deriveServiceNotifications';
import { compareNotifications, deriveNotifications, type AppNotification } from '../lib/deriveNotifications';

export interface NotificationsState {
  notifications: AppNotification[];
  isPending: boolean;
  isError: boolean;
  /** The accounts (settled, well or not) and the Portfolio notices (where the items that need the owner come from) have been read. */
  ready: boolean;
  refetch: () => void;
  /** The clock the list was derived at, for relative times. */
  now: number;
}

/**
 * All current notifications: the accounts' (the API-key expiry, derived here from their state), the data, service, Portfolio
 * and plan notices (derived by the API, among them the items that need the owner), open ones first and most urgent first. The notices
 * are polled lightly; when they cannot be read the account notifications still show.
 */
export function useNotifications(): NotificationsState {
  const { data, isPending, isError, refetch } = useQuery(accountsQuery);
  const now = useNow(true, data ? data.map((a) => a.connection.expiresAt ?? '').join('|') : null);
  const notices = useQuery(dataNoticesQuery);
  const noticeItems = notices.data?.items;
  const serviceNotices = useQuery(serviceNoticesQuery);
  const serviceItems = serviceNotices.data?.items;
  const portfolioNotices = useQuery(portfolioNoticesQuery);
  const portfolioItems = portfolioNotices.data?.items;
  const planNotices = useQuery(planNoticesQuery);
  const planItems = planNotices.data?.items;
  const notifications = useMemo(
    () =>
      [
        ...deriveNotifications(data ?? [], now),
        ...deriveDataNotifications(noticeItems ?? []),
        ...deriveServiceNotifications(serviceItems ?? []),
        ...derivePortfolioNotifications(portfolioItems ?? []),
        ...derivePlanNotifications(planItems ?? []),
      ].sort(compareNotifications),
    [data, now, noticeItems, serviceItems, portfolioItems, planItems],
  );
  return {
    notifications,
    isPending,
    isError,
    ready: !isPending && portfolioNotices.isSuccess,
    now,
    refetch: () => {
      void refetch();
    },
  };
}
