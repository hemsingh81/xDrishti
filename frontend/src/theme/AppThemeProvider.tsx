import { CssBaseline, ThemeProvider, useMediaQuery } from '@mui/material';
import { useCallback, useMemo, useState, type ReactNode } from 'react';
import { ColorModeContext } from './ColorModeContext';
import { createAppTheme } from './theme';
import type { ColorMode } from './tokens';

const STORAGE_KEY = 'xd-color-mode';

function readStoredMode(): ColorMode | null {
  try {
    const value = localStorage.getItem(STORAGE_KEY);
    return value === 'light' || value === 'dark' ? value : null;
  } catch {
    return null; // storage blocked — fall back to the system preference
  }
}

export function AppThemeProvider({ children }: { children: ReactNode }) {
  const prefersDark = useMediaQuery('(prefers-color-scheme: dark)');
  const [stored, setStored] = useState<ColorMode | null>(readStoredMode);
  const mode: ColorMode = stored ?? (prefersDark ? 'dark' : 'light');

  const toggle = useCallback(() => {
    const next: ColorMode = mode === 'dark' ? 'light' : 'dark';
    setStored(next);
    try {
      localStorage.setItem(STORAGE_KEY, next);
    } catch {
      // ignore: preference simply is not remembered
    }
  }, [mode]);

  const theme = useMemo(() => createAppTheme(mode), [mode]);
  const value = useMemo(() => ({ mode, toggle }), [mode, toggle]);

  return (
    <ColorModeContext value={value}>
      <ThemeProvider theme={theme}>
        <CssBaseline enableColorScheme />
        {children}
      </ThemeProvider>
    </ColorModeContext>
  );
}
