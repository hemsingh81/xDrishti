import { alpha, useTheme } from '@mui/material/styles';
import { iconChip, iconHues, type ColorMode, type IconHueName } from './tokens';

export interface ResolvedHue {
  /** Icon colour: the strong value in light mode, the light value in dark mode. */
  color: string;
  /** Chip background: the icon colour at a soft tint. */
  background: string;
}

/** Pure resolution (also used by the contrast test). */
export function resolveIconHue(hue: IconHueName, mode: ColorMode): ResolvedHue {
  const pair = iconHues[hue];
  const color = mode === 'dark' ? pair.light : pair.strong;
  return { color, background: alpha(color, mode === 'dark' ? iconChip.tintDark : iconChip.tintLight) };
}

/** Resolves a hue token name for the active colour mode. Components never read raw hue values. */
export function useIconHue(hue: IconHueName): ResolvedHue {
  return resolveIconHue(hue, useTheme().palette.mode);
}
