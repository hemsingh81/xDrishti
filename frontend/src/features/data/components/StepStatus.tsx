import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import ErrorIcon from '@mui/icons-material/ErrorOutlineOutlined';
import RemoveCircleIcon from '@mui/icons-material/RemoveCircleOutlineOutlined';
import ScheduleIcon from '@mui/icons-material/ScheduleOutlined';
import SyncIcon from '@mui/icons-material/Sync';
import { Chip } from '@mui/material';
import type { ReactElement } from 'react';
import { phaseStatusLabel } from '../lib/phases';

interface Look {
  color: 'success' | 'error' | 'info' | 'default';
  icon: ReactElement;
}

const looks: Readonly<Record<string, Look>> = {
  succeeded: { color: 'success', icon: <CheckCircleIcon /> },
  failed: { color: 'error', icon: <ErrorIcon /> },
  skipped: { color: 'default', icon: <RemoveCircleIcon /> },
  running: { color: 'info', icon: <SyncIcon /> },
  pending: { color: 'default', icon: <ScheduleIcon /> },
};

/** The outcome of a step as a chip with an icon and a word; `null` = the step never ran. */
export function StepStatus({ status }: { status: string | null }) {
  const look = status ? looks[status] : undefined;
  return (
    <Chip
      size="small"
      variant="outlined"
      color={look?.color ?? 'default'}
      icon={look?.icon}
      label={status ? phaseStatusLabel(status) : 'Not run yet'}
    />
  );
}
