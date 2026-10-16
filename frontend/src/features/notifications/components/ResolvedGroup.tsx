import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import { Box, Button, Collapse } from '@mui/material';
import { useId, useState } from 'react';
import type { AppNotification, NotificationAction } from '../lib/deriveNotifications';
import { NotificationList } from './NotificationList';

interface ResolvedGroupProps {
  items: readonly AppNotification[];
  readIds: ReadonlySet<string>;
  now: number;
  busy: string | null;
  onOpen: (notification: AppNotification) => void;
  onAction: (notification: AppNotification, action: NotificationAction) => void;
}

/** The items whose cause is gone (kept 24 h): collapsed until the owner asks for them. */
export function ResolvedGroup({ items, ...rest }: ResolvedGroupProps) {
  const [open, setOpen] = useState(false);
  const regionId = useId();
  return (
    <Box component="section" aria-label="Resolved" sx={{ mt: 1 }}>
      <Button
        size="small"
        color="inherit"
        aria-expanded={open}
        aria-controls={regionId}
        endIcon={
          <ExpandMoreIcon
            sx={{
              transform: open ? 'rotate(180deg)' : 'none',
              transition: (theme) => theme.transitions.create('transform'),
            }}
          />
        }
        onClick={() => {
          setOpen((value) => !value);
        }}
        sx={{ color: 'text.secondary' }}
      >
        Resolved ({items.length})
      </Button>
      <Collapse in={open} unmountOnExit id={regionId}>
        <NotificationList label="Resolved notifications" items={items} {...rest} />
      </Collapse>
    </Box>
  );
}
