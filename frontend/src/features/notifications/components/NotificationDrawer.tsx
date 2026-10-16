import CloseIcon from '@mui/icons-material/Close';
import { Box, Button, Divider, Drawer, IconButton, Stack, Typography } from '@mui/material';
import { useId } from 'react';
import { ErrorState, LoadingState } from '@/shared/ui';
import type { NotificationsState } from '../hooks/useNotifications';
import { useRunNotificationAction } from '../hooks/useRunNotificationAction';
import type { AppNotification, NotificationAction } from '../lib/deriveNotifications';
import { stateKey } from '../lib/readState';
import { NotificationList } from './NotificationList';
import { ResolvedGroup } from './ResolvedGroup';

interface NotificationDrawerProps {
  open: boolean;
  onClose: () => void;
  state: NotificationsState;
  readIds: ReadonlySet<string>;
  onMarkRead: (items: readonly AppNotification[]) => void;
}

/**
 * Right-hand "Notifications" window: what is open (most urgent first, then newest), what was resolved in the last 24 hours
 * (collapsed), "Mark all read", and a calm empty state. An item's buttons go to a page (the window closes) or run an
 * existing endpoint (the notices are read again and a toast tells the result).
 */
export function NotificationDrawer({ open, onClose, state, readIds, onMarkRead }: NotificationDrawerProps) {
  const titleId = useId();
  const { notifications, isPending, isError, now, refetch } = state;
  const unresolved = notifications.filter((n) => !n.resolved);
  const resolved = notifications.filter((n) => n.resolved);
  const unread = unresolved.filter((n) => !readIds.has(stateKey(n)));
  const { run, busy } = useRunNotificationAction();

  const onOpen = (item: AppNotification) => {
    onMarkRead([item]);
    onClose();
  };
  const onAction = (item: AppNotification, action: NotificationAction) => {
    onMarkRead([item]);
    // A page replaces the window; a command keeps it open to show the item go.
    if (action.kind === 'route') onClose();
    void run(action, `${item.id}|${action.label}`);
  };

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
            onMarkRead(notifications);
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
        {!isPending && !isError && unresolved.length === 0 && (
          <Typography color="text.secondary" sx={{ py: 4, textAlign: 'center' }}>
            You&apos;re all caught up
          </Typography>
        )}
        {unresolved.length > 0 && (
          <Box component="section" aria-label="Unresolved">
            {resolved.length > 0 && (
              <Typography variant="overline" component="h3" color="text.secondary" sx={{ display: 'block', pt: 1 }}>
                Unresolved
              </Typography>
            )}
            <NotificationList
              label="Notifications"
              items={unresolved}
              readIds={readIds}
              now={now}
              busy={busy}
              onOpen={onOpen}
              onAction={onAction}
            />
          </Box>
        )}
        {resolved.length > 0 && (
          <ResolvedGroup items={resolved} readIds={readIds} now={now} busy={busy} onOpen={onOpen} onAction={onAction} />
        )}
      </Box>
    </Drawer>
  );
}
