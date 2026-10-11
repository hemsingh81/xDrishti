import CloseIcon from '@mui/icons-material/Close';
import { Box, Button, Divider, Drawer, IconButton, Stack, Typography } from '@mui/material';
import { useId } from 'react';
import { ErrorState, LoadingState } from '@/shared/ui';
import type { NotificationsState } from '../hooks/useNotifications';
import type { AppNotification } from '../lib/deriveNotifications';
import { NotificationItem } from './NotificationItem';

interface NotificationDrawerProps {
  open: boolean;
  onClose: () => void;
  state: NotificationsState;
  readIds: ReadonlySet<string>;
  onMarkRead: (ids: readonly string[]) => void;
}

/** Right-hand "Notifications" window: newest and most urgent first, "Mark all read", and a calm empty state. */
export function NotificationDrawer({ open, onClose, state, readIds, onMarkRead }: NotificationDrawerProps) {
  const titleId = useId();
  const { notifications, isPending, isError, now, refetch } = state;
  const unread = notifications.filter((n) => !readIds.has(n.id));

  return (
    <Drawer
      anchor="right"
      open={open}
      onClose={onClose}
      slotProps={{
        paper: { role: 'dialog', 'aria-labelledby': titleId, sx: { width: 400, maxWidth: '100vw' } },
      }}
    >
      <Stack direction="row" sx={{ alignItems: 'center', gap: 1, px: 2.5, py: 1.5 }}>
        <Typography id={titleId} component="h2" variant="h2" sx={{ flex: 1 }}>
          Notifications
        </Typography>
        <Button
          size="small"
          disabled={unread.length === 0}
          onClick={() => {
            onMarkRead(notifications.map((n) => n.id));
          }}
        >
          Mark all read
        </Button>
        <IconButton aria-label="Close notifications" size="small" edge="end" onClick={onClose}>
          <CloseIcon fontSize="small" />
        </IconButton>
      </Stack>
      <Divider />
      <Box sx={{ px: 2.5, overflowY: 'auto', flex: 1 }}>
        {isPending && <LoadingState label="Loading notifications…" />}
        {isError && !isPending && (
          <Stack spacing={1} sx={{ alignItems: 'flex-start' }}>
            <ErrorState error={new Error('Could not load the notifications.')} />
            <Button size="small" onClick={refetch}>
              Try again
            </Button>
          </Stack>
        )}
        {!isPending && !isError && notifications.length === 0 && (
          <Typography color="text.secondary" sx={{ py: 4, textAlign: 'center' }}>
            You&apos;re all caught up
          </Typography>
        )}
        {notifications.length > 0 && (
          <Stack
            component="ul"
            aria-label="Notifications"
            divider={<Divider component="li" />}
            sx={{ m: 0, p: 0, listStyle: 'none' }}
          >
            {notifications.map((n: AppNotification) => (
              <NotificationItem
                key={n.id}
                notification={n}
                unread={!readIds.has(n.id)}
                now={now}
                onOpen={(item) => {
                  onMarkRead([item.id]);
                  onClose();
                }}
              />
            ))}
          </Stack>
        )}
      </Box>
    </Drawer>
  );
}
