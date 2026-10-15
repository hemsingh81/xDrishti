import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import ErrorIcon from '@mui/icons-material/ErrorOutlined';
import InfoIcon from '@mui/icons-material/InfoOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Box, Button, Link as MuiLink, Stack, Typography } from '@mui/material';
import { visuallyHidden } from '@mui/utils';
import { Link } from 'react-router';
import { formatAgo } from '@/shared/lib/format';
import { IconChip } from '@/shared/ui';
import type { IconHueName } from '@/theme';
import type { AppNotification, NotificationAction, NotificationSeverity } from '../lib/deriveNotifications';

const look: Record<NotificationSeverity, { icon: typeof ErrorIcon; hue: IconHueName; label: string }> = {
  error: { icon: ErrorIcon, hue: 'rose', label: 'Error' },
  warn: { icon: WarningIcon, hue: 'amber', label: 'Warning' },
  info: { icon: InfoIcon, hue: 'sky', label: 'Information' },
};
const resolvedLook = { icon: CheckCircleIcon, hue: 'green' as IconHueName, label: 'Resolved' };

interface NotificationItemProps {
  notification: AppNotification;
  unread: boolean;
  now: number;
  /** The `id|label` of the command that is running, to hold its button. */
  busy: string | null;
  /** Following Open or pressing a button: the owner has seen it. */
  onOpen: (notification: AppNotification) => void;
  /** A button of the notification was pressed. */
  onAction: (notification: AppNotification, action: NotificationAction) => void;
}

/**
 * One notification: severity chip (icon + hidden word, not colour alone), the title, the text, the relative time and up to two
 * buttons; a notice without buttons has an Open link. A resolved one is greyed, says so and has no buttons.
 */
export function NotificationItem({ notification, unread, now, busy, onOpen, onAction }: NotificationItemProps) {
  const { resolved, actions, title, text } = notification;
  const { icon, hue, label } = resolved ? resolvedLook : look[notification.severity];
  const weight = unread && !resolved ? 650 : 400;
  const prefix = (
    <Box component="span" sx={visuallyHidden}>
      {label}: {unread && !resolved ? 'unread, ' : ''}
    </Box>
  );
  const shownActions = resolved ? [] : actions;
  return (
    <Stack component="li" direction="row" spacing={1.5} sx={{ py: 1.5, alignItems: 'flex-start' }}>
      <IconChip icon={icon} hue={hue} size="md" />
      <Box sx={{ flex: 1, minWidth: 0 }}>
        {title ? (
          <>
            <Typography sx={{ fontWeight: weight, overflowWrap: 'anywhere' }}>
              {prefix}
              {title}
            </Typography>
            <Typography variant="body2" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>
              {text}
            </Typography>
          </>
        ) : (
          <Typography sx={{ fontWeight: weight, overflowWrap: 'anywhere' }}>
            {prefix}
            {text}
          </Typography>
        )}
        <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center', mt: 0.25 }}>
          {notification.at !== null && (
            <Typography variant="caption" color="text.secondary" className="numeric">
              {resolved ? 'Resolved · ' : ''}
              {formatAgo(new Date(notification.at), new Date(now))}
            </Typography>
          )}
          {!resolved && actions.length === 0 && (
            <MuiLink
              component={Link}
              variant="body2"
              to={notification.href}
              aria-label={`Open: ${text}`}
              onClick={() => {
                onOpen(notification);
              }}
            >
              Open
            </MuiLink>
          )}
        </Stack>
        {shownActions.length > 0 && (
          <Stack
            role="group"
            aria-label={`Actions: ${title ?? text}`}
            direction="row"
            sx={{ gap: 1, flexWrap: 'wrap', mt: 1 }}
          >
            {shownActions.map((action, index) => (
              <Button
                key={`${action.label}|${action.target}`}
                size="small"
                variant={index === 0 ? 'contained' : 'outlined'}
                loading={busy === `${notification.id}|${action.label}`}
                disabled={busy !== null}
                onClick={() => {
                  onAction(notification, action);
                }}
              >
                {action.label}
              </Button>
            ))}
          </Stack>
        )}
      </Box>
    </Stack>
  );
}
