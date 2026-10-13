import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import ErrorIcon from '@mui/icons-material/ErrorOutlineOutlined';
import PauseIcon from '@mui/icons-material/PauseCircleOutlineOutlined';
import SyncIcon from '@mui/icons-material/Sync';
import { Chip } from '@mui/material';
import { serviceStatusLabel } from '../lib/labels';

const looks = {
  healthy: { color: 'success', icon: <CheckCircleIcon /> },
  running: { color: 'info', icon: <SyncIcon /> },
  failed: { color: 'error', icon: <ErrorIcon /> },
  paused: { color: 'default', icon: <PauseIcon /> },
} as const;

/** A service's state as an icon and a word (Healthy · Running · Failed · Paused), never by colour alone. */
export function ServiceStatusChip({ status }: { status: string }) {
  const look = status in looks ? looks[status as keyof typeof looks] : undefined;
  return (
    <Chip
      size="small"
      variant="outlined"
      color={look?.color ?? 'default'}
      icon={look?.icon}
      label={serviceStatusLabel(status)}
    />
  );
}
