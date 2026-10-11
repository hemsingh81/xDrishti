import { useQuery } from '@tanstack/react-query';
import { useMemo } from 'react';
import { accountsQuery, useNow } from '@/features/accounts';
import { deriveNotifications, type AppNotification } from '../lib/deriveNotifications';

export interface NotificationsState {
  notifications: AppNotification[];
  isPending: boolean;
  isError: boolean;
  refetch: () => void;
  /** The clock the list was derived at, for relative times. */
  now: number;
}

/** All current notifications. Today they come from the accounts; other sources are merged in here. */
export function useNotifications(): NotificationsState {
  const { data, isPending, isError, refetch } = useQuery(accountsQuery);
  const now = useNow(true, data ? data.map((a) => a.connection.expiresAt ?? '').join('|') : null);
  const notifications = useMemo(() => deriveNotifications(data ?? [], now), [data, now]);
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
