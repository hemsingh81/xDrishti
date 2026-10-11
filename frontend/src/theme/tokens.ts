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

export const shape = { radius: 12, radiusSmall: 8, radiusLarge: 18 } as const;

/** Dialog band padding (px) — from the prototype's `.dlg-h`, `.dlg-b`, `.dlg-f`. */
export const dialogSpacing = { header: '15px 20px', body: '18px 20px', footer: '13px 20px' } as const;

export const fontFamily = "'Inter Variable', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif";

/** Monospace for values the owner copies or compares character by character (URLs, ids). */
export const monoFontFamily = 'ui-monospace, SFMono-Regular, Menlo, Consolas, monospace';

export const layout = { sidebarWidth: 248, topBarHeight: 60 } as const;

/** Starting colours offered for an account's colour bar; the chosen value is user data stored with the account. */
export const accountColors = ['#5b8def', '#16a34a', '#b45309', '#a855f7', '#0891b2', '#dc2626'] as const;
