import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import ErrorIcon from '@mui/icons-material/ErrorOutlineOutlined';
import RemoveCircleIcon from '@mui/icons-material/RemoveCircleOutlineOutlined';
import SyncIcon from '@mui/icons-material/Sync';
import type { SvgIconComponent } from '@mui/icons-material';
import type { EodTone } from '../lib/eod';

/** The icon of the last automatic run's outcome (the word is said next to it). */
export const runIcons: Readonly<Record<string, SvgIconComponent>> = {
  succeeded: CheckCircleIcon,
  failed: ErrorIcon,
  skipped: RemoveCircleIcon,
  running: SyncIcon,
};

/** The colour of an outcome tone. */
export const toneColour: Readonly<Record<EodTone, string>> = {
  success: 'success.main',
  error: 'error.main',
  warning: 'warning.main',
  neutral: 'text.secondary',
};
