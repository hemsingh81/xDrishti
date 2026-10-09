/**
 * Design tokens — the single source for colours, radii and typography (from docs/prototype).
 * Components read these through the MUI theme, never as hard-coded values.
 */
export type ColorMode = 'light' | 'dark';

export const palettes = {
  light: {
    background: '#f6f7fb',
    surface: '#ffffff',
    surfaceAlt: '#f8fafc',
    border: '#e5e7eb',
    text: '#1f2937',
    textSecondary: '#6b7280',
    accent: '#4f46e5',
    accentSoft: '#eef2ff',
    good: '#16a34a',
    bad: '#dc2626',
    warn: '#b45309',
    info: '#0891b2',
  },
  dark: {
    background: '#111827',
    surface: '#1f2937',
    surfaceAlt: '#1a2230',
    border: '#2f3a4b',
    text: '#e5e7eb',
    textSecondary: '#9ca3af',
    accent: '#818cf8',
    accentSoft: '#272a4a',
    good: '#4ade80',
    bad: '#f87171',
    warn: '#fbbf24',
    info: '#67e8f9',
  },
} as const satisfies Record<ColorMode, Record<string, string>>;

export const shape = { radius: 12, radiusSmall: 8 } as const;

export const fontFamily = "'Inter Variable', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif";

export const layout = { sidebarWidth: 248, topBarHeight: 60 } as const;
