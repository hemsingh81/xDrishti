import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import ErrorIcon from '@mui/icons-material/ErrorOutlineOutlined';
import RemoveCircleIcon from '@mui/icons-material/RemoveCircleOutlineOutlined';
import SyncIcon from '@mui/icons-material/Sync';
import { Chip } from '@mui/material';
import { runStatusLabel } from '../lib/labels';

const looks = {
  succeeded: { color: 'success', icon: <CheckCircleIcon /> },
  failed: { color: 'error', icon: <ErrorIcon /> },
  running: { color: 'info', icon: <SyncIcon /> },
  skipped: { color: 'warning', icon: <RemoveCircleIcon /> },
} as const;

/** How a run ended (Succeeded · Failed · Running · Skipped) as an icon and a word. */
export function RunStatusChip({ status }: { status: string }) {
  const look = status in looks ? looks[status as keyof typeof looks] : undefined;
  return (
    <Chip
      size="small"
      variant="outlined"
      color={look?.color ?? 'default'}
      icon={look?.icon}
      label={runStatusLabel(status)}
    />
  );
}
