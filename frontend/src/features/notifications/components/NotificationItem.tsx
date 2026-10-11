import ErrorIcon from '@mui/icons-material/ErrorOutlined';
import InfoIcon from '@mui/icons-material/InfoOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Box, Link as MuiLink, Stack, Typography } from '@mui/material';
import { visuallyHidden } from '@mui/utils';
import { Link } from 'react-router';
import { formatAgo } from '@/shared/lib/format';
import { IconChip } from '@/shared/ui';
import type { IconHueName } from '@/theme';
import type { AppNotification, NotificationSeverity } from '../lib/deriveNotifications';

const look: Record<NotificationSeverity, { icon: typeof ErrorIcon; hue: IconHueName; label: string }> = {
  error: { icon: ErrorIcon, hue: 'rose', label: 'Error' },
  warn: { icon: WarningIcon, hue: 'amber', label: 'Warning' },
  info: { icon: InfoIcon, hue: 'sky', label: 'Information' },
};

interface NotificationItemProps {
  notification: AppNotification;
  unread: boolean;
  now: number;
  /** Following Open: the owner has seen it. */
  onOpen: (notification: AppNotification) => void;
}

/** One notification: severity chip (icon + hidden word, not colour alone), text, relative time and an Open link. */
export function NotificationItem({ notification, unread, now, onOpen }: NotificationItemProps) {
  const { icon, hue, label } = look[notification.severity];
  return (
    <Stack component="li" direction="row" spacing={1.5} sx={{ py: 1.5, alignItems: 'flex-start' }}>
      <IconChip icon={icon} hue={hue} size="md" />
      <Box sx={{ flex: 1, minWidth: 0 }}>
        <Typography sx={{ fontWeight: unread ? 650 : 400, overflowWrap: 'anywhere' }}>
          <Box component="span" sx={visuallyHidden}>
            {label}: {unread ? 'unread, ' : ''}
          </Box>
          {notification.text}
        </Typography>
        <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center', mt: 0.25 }}>
          {notification.at !== null && (
            <Typography variant="caption" color="text.secondary" className="numeric">
              {formatAgo(new Date(notification.at), new Date(now))}
            </Typography>
          )}
          <MuiLink
            component={Link}
            variant="body2"
            to={`/accounts?account=${notification.accountId}&tab=connection`}
            aria-label={`Open: ${notification.text}`}
            onClick={() => {
              onOpen(notification);
            }}
          >
            Open
          </MuiLink>
        </Stack>
      </Box>
    </Stack>
  );
}
