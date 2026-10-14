import type { SvgIconComponent } from '@mui/icons-material';
import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import ErrorIcon from '@mui/icons-material/ErrorOutlineOutlined';
import InfoIcon from '@mui/icons-material/InfoOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { palettes, type ColorMode, type IconHueName } from '@/theme';
import type { ToastSeverity } from './toastContext';

type PaletteKey = keyof (typeof palettes)['light'];

/**
 * Which palette tokens colour a toast of each severity: the colour (accent bar, buttons, close) and its soft tint (the
 * card). The same pairs as the banners (`MuiAlert`): success green, info indigo (the accent), warning amber, error rose.
 */
export const toastPaletteKeys = {
  success: { color: 'good', soft: 'goodSoft' },
  info: { color: 'accent', soft: 'accentSoft' },
  warning: { color: 'warn', soft: 'warnSoft' },
  error: { color: 'bad', soft: 'badSoft' },
} as const satisfies Record<ToastSeverity, { color: PaletteKey; soft: PaletteKey }>;

const chips = {
  success: { icon: CheckCircleIcon, hue: 'green' },
  info: { icon: InfoIcon, hue: 'indigo' },
  warning: { icon: WarningIcon, hue: 'amber' },
  error: { icon: ErrorIcon, hue: 'rose' },
} as const satisfies Record<ToastSeverity, { icon: SvgIconComponent; hue: IconHueName }>;

export interface ToastTone {
  icon: SvgIconComponent;
  hue: IconHueName;
  /** Accent bar, buttons, close button, countdown. */
  color: string;
  /** Opaque card surface (a toast lies over any content, so it is never translucent). */
  soft: string;
}

export function resolveToastTone(severity: ToastSeverity, mode: ColorMode): ToastTone {
  const keys = toastPaletteKeys[severity];
  const palette = palettes[mode];
  return { ...chips[severity], color: palette[keys.color], soft: palette[keys.soft] };
}

/** Errors and warnings interrupt (`alert`), the rest wait for a pause (`status`). One role per card: announced once. */
export const toastRole = (severity: ToastSeverity): 'alert' | 'status' =>
  severity === 'error' || severity === 'warning' ? 'alert' : 'status';
