import NotificationsIcon from '@mui/icons-material/NotificationsNoneOutlined';
import { Badge, IconButton, Tooltip } from '@mui/material';
import { useState } from 'react';
import { useForgetResolved } from '../hooks/useForgetResolved';
import { useNotifications } from '../hooks/useNotifications';
import { useReadState } from '../hooks/useReadState';
import { stateKey, type ReadStorage } from '../lib/readState';
import { NotificationDrawer } from './NotificationDrawer';

export interface NotificationBellProps {
  /** Where read state is kept; defaults to localStorage. Tests pass a fake, `null` turns persistence off. */
  storage?: ReadStorage | null;
}

/** Top-bar bell with the unread count; opens the notifications drawer. Usable while loading or after an error. */
export function NotificationBell({ storage }: NotificationBellProps) {
  const state = useNotifications();
  const { readIds, markItemsRead, forget } = useReadState(storage);
  useForgetResolved(state.notifications, forget);
  const [open, setOpen] = useState(false);
  const unread = state.notifications.filter((n) => !n.resolved && !readIds.has(stateKey(n))).length;

  return (
    <>
      <Tooltip title="Notifications">
        <IconButton
          aria-label={unread > 0 ? `Notifications, ${unread} unread` : 'Notifications'}
          aria-haspopup="dialog"
          onClick={() => {
            setOpen(true);
          }}
        >
          <Badge badgeContent={unread} max={9} color="error">
            <NotificationsIcon />
          </Badge>
        </IconButton>
      </Tooltip>
      <NotificationDrawer
        open={open}
        onClose={() => {
          setOpen(false);
        }}
        state={state}
        readIds={readIds}
        onMarkRead={markItemsRead}
      />
    </>
  );
}
