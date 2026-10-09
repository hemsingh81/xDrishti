import { createContext, useContext } from 'react';
import type { ColorMode } from './tokens';

export interface ColorModeState {
  mode: ColorMode;
  toggle: () => void;
}

export const ColorModeContext = createContext<ColorModeState | null>(null);

export function useColorMode(): ColorModeState {
  const value = useContext(ColorModeContext);
  if (!value) {
    throw new Error('useColorMode must be used inside <AppThemeProvider>');
  }
  return value;
}
