import { useQuery } from '@tanstack/react-query';
import { useMemo } from 'react';
import { accountsQuery, useNow } from '@/features/accounts';
import { dataNoticesQuery, serviceNoticesQuery } from '@/shared/api';
import { deriveDataNotifications } from '../lib/deriveDataNotifications';
import { deriveServiceNotifications } from '../lib/deriveServiceNotifications';
import { compareNotifications, deriveNotifications, type AppNotification } from '../lib/deriveNotifications';

export interface NotificationsState {
  notifications: AppNotification[];
  isPending: boolean;
  isError: boolean;
  refetch: () => void;
  /** The clock the list was derived at, for relative times. */
  now: number;
}

/**
 * All current notifications: the accounts' (derived here from their state), the data notices and the service notices
 * (both derived by the API), most urgent first. The notices are polled lightly; when they cannot be read the account
 * notifications still show.
 */
export function useNotifications(): NotificationsState {
  const { data, isPending, isError, refetch } = useQuery(accountsQuery);
  const now = useNow(true, data ? data.map((a) => a.connection.expiresAt ?? '').join('|') : null);
  const notices = useQuery(dataNoticesQuery);
  const noticeItems = notices.data?.items;
  const serviceNotices = useQuery(serviceNoticesQuery);
  const serviceItems = serviceNotices.data?.items;
  const notifications = useMemo(
    () =>
      [
        ...deriveNotifications(data ?? [], now),
        ...deriveDataNotifications(noticeItems ?? []),
        ...deriveServiceNotifications(serviceItems ?? []),
      ].sort(compareNotifications),
    [data, now, noticeItems, serviceItems],
  );
  return {
    notifications,
    isPending,
    isError,
    now,
    refetch: () => {
      void refetch();
    },
  };
}
