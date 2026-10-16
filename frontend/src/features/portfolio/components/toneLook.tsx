import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import ErrorIcon from '@mui/icons-material/ErrorOutlineOutlined';
import SyncIcon from '@mui/icons-material/Sync';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import type { ReactElement } from 'react';
import type { RunTone } from '../lib/syncText';

/** The icon and colour of a run's outcome (the words always say it too: colour is never the only sign). */
export const toneLook: Record<RunTone, { icon: ReactElement; color: string }> = {
  success: { icon: <CheckCircleIcon fontSize="small" />, color: 'success.main' },
  error: { icon: <ErrorIcon fontSize="small" />, color: 'error.main' },
  warning: { icon: <WarningIcon fontSize="small" />, color: 'warning.main' },
  info: { icon: <SyncIcon fontSize="small" />, color: 'info.main' },
};
