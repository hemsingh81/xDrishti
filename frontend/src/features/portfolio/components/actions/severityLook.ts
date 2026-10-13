import ErrorIcon from '@mui/icons-material/ErrorOutlined';
import InfoIcon from '@mui/icons-material/InfoOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import type { IconHueName } from '@/theme';
import type { Severity } from '../../lib/actions';

/** The icon and hue of a severity. The word always goes with it: colour and shape never carry the meaning alone. */
export const severityLook: Record<Severity, { icon: typeof ErrorIcon; hue: IconHueName }> = {
  high: { icon: ErrorIcon, hue: 'rose' },
  medium: { icon: WarningIcon, hue: 'amber' },
  info: { icon: InfoIcon, hue: 'sky' },
};
