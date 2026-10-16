import { Divider, Stack } from '@mui/material';
import type { AppNotification, NotificationAction } from '../lib/deriveNotifications';
import { stateKey } from '../lib/readState';
import { NotificationItem } from './NotificationItem';

interface NotificationListProps {
  label: string;
  items: readonly AppNotification[];
  readIds: ReadonlySet<string>;
  now: number;
  busy: string | null;
  onOpen: (notification: AppNotification) => void;
  onAction: (notification: AppNotification, action: NotificationAction) => void;
}

/** A labelled list of notifications with dividers between them. */
export function NotificationList({ label, items, readIds, now, busy, onOpen, onAction }: NotificationListProps) {
  return (
    <Stack
      component="ul"
      aria-label={label}
      divider={<Divider component="li" />}
      sx={{ m: 0, p: 0, listStyle: 'none' }}
    >
      {items.map((n) => (
        <NotificationItem
          key={n.id}
          notification={n}
          unread={!readIds.has(stateKey(n))}
          now={now}
          busy={busy}
          onOpen={onOpen}
          onAction={onAction}
        />
      ))}
    </Stack>
  );
}
