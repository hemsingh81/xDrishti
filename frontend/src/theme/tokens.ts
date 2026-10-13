/**
 * Design tokens — the single source for colours, radii and typography (from docs/prototype).
 * Components read these through the MUI theme, never as hard-coded values.
 */
export type ColorMode = 'light' | 'dark';

export const palettes = {
  light: {
    background: '#f4f6fa',
    surface: '#ffffff',
    surfaceAlt: '#f1f4f9',
    border: '#dfe3ea',
    inputBorder: '#7b8798',
    text: '#0f172a',
    textSecondary: '#475569',
    accent: '#4338ca',
    accentContrast: '#ffffff',
    accentSoft: '#eceefe',
    good: '#146c36',
    bad: '#b91c1c',
    warn: '#a35200',
    info: '#0e7490',
  },
  dark: {
    background: '#0b1220',
    surface: '#131c2e',
    surfaceAlt: '#182238',
    border: '#26324a',
    inputBorder: '#5f6f8a',
    text: '#e6eaf2',
    textSecondary: '#9aa7bd',
    accent: '#9aa5ff',
    accentContrast: '#0b1220',
    accentSoft: '#222a52',
    good: '#4ade80',
    bad: '#f87171',
    warn: '#fbbf24',
    info: '#67e8f9',
  },
} as const satisfies Record<ColorMode, Record<string, string>>;

/**
 * Categorical palette for charts that colour categories (an allocation donut, a stacked bar): eight series colours and a
 * ninth, a neutral, for "Other". Each is at least 3:1 against the card (`surface`) in its mode (`contrast.test.ts`), and the
 * charts never rely on colour alone (names and percentages are in labels, legend and table). The light set is the
 * darker, the dark set the lighter variant of the same hues.
 */
export const categorical = {
  light: ['#4f46e5', '#c2410c', '#0f766e', '#db2777', '#0284c7', '#a16207', '#15803d', '#9333ea', '#64748b'],
  dark: ['#818cf8', '#fb923c', '#2dd4bf', '#f472b6', '#38bdf8', '#fbbf24', '#4ade80', '#c084fc', '#94a3b8'],
} as const satisfies Record<ColorMode, readonly string[]>;

/** How many categories get their own colour; the next one (index `categoricalSeries`) is the neutral "Other". */
export const categoricalSeries = 8;

export const shape = { radius: 12, radiusSmall: 8, radiusLarge: 18 } as const;

/** Dialog band padding (px) — from the prototype's `.dlg-h`, `.dlg-b`, `.dlg-f`. */
export const dialogSpacing = { header: '15px 20px', body: '18px 20px', footer: '13px 20px' } as const;

/** Metric-matched local fallbacks (declared in fonts.css) keep the font swap from shifting the layout. */
export const fontFamily =
  "'Inter Variable', 'Inter Fallback', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif";

/** Monospace for values the owner copies or compares character by character (URLs, ids). */
export const monoFontFamily =
  "'JetBrains Mono Variable', 'JetBrains Mono Fallback', ui-monospace, Menlo, Consolas, monospace";

/** Type scale on a 14 px base. `size` is rem; `lineHeight` unitless; `tracking` em. */
export const typeScale = {
  h1: { size: 1.5, weight: 700, lineHeight: 1.25, tracking: -0.02 },
  h2: { size: 1.125, weight: 650, lineHeight: 1.3, tracking: -0.01 },
  h3: { size: 0.95, weight: 650, lineHeight: 1.35, tracking: -0.01 },
  h4: { size: 1, weight: 650, lineHeight: 1.4, tracking: 0 },
  h5: { size: 13 / 14, weight: 650, lineHeight: 1.4, tracking: 0 },
  h6: { size: 12 / 14, weight: 650, lineHeight: 1.4, tracking: 0 },
  subtitle1: { size: 1, weight: 600, lineHeight: 1.45, tracking: 0 },
  subtitle2: { size: 13 / 14, weight: 600, lineHeight: 1.45, tracking: 0 },
  body1: { size: 1, weight: 400, lineHeight: 1.5, tracking: 0 },
  body2: { size: 13 / 14, weight: 400, lineHeight: 1.45, tracking: 0 },
  caption: { size: 12 / 14, weight: 400, lineHeight: 1.4, tracking: 0 },
  overline: { size: 11 / 14, weight: 600, lineHeight: 1.4, tracking: 0.06 },
  button: { size: 1, weight: 600, lineHeight: 1.4, tracking: 0 },
} as const;

/** Numeric roles (tables, stat tiles, countdowns): aligned digits and a slashed zero. */
export const numeric = { fontVariantNumeric: 'tabular-nums', fontFeatureSettings: "'cv11', 'zero'" } as const;

/** Elevation per mode: soft cool-tinted shadows in light, border-led (tighter, darker) in dark. */
export const elevation = {
  light: {
    sm: '0 1px 2px rgba(15, 23, 42, 0.06), 0 1px 1px rgba(15, 23, 42, 0.04)',
    md: '0 4px 12px rgba(15, 23, 42, 0.08), 0 1px 3px rgba(15, 23, 42, 0.06)',
    lg: '0 12px 32px rgba(15, 23, 42, 0.14), 0 2px 6px rgba(15, 23, 42, 0.08)',
  },
  dark: {
    sm: '0 1px 2px rgba(0, 0, 0, 0.5)',
    md: '0 4px 12px rgba(0, 0, 0, 0.55)',
    lg: '0 12px 32px rgba(0, 0, 0, 0.65)',
  },
} as const satisfies Record<ColorMode, Record<'sm' | 'md' | 'lg', string>>;

/** Focus ring: 2 px accent outline, 2 px offset, on every interactive element (`:focus-visible`). */
/** Opacity of placeholder text (colour is `textSecondary`) and of disabled text / fills (`textSecondary` over the surface). */
/** `busy`: opacity of content that is stale (rows while the next page loads) or of a control that is busy. */
export const stateAlpha = { placeholder: 0.9, disabledText: 0.75, disabledFill: 0.12, busy: 0.6 } as const;

export const focusRing = { width: 2, offset: 2 } as const;

export const motion = {
  fast: '120ms',
  base: '200ms',
  easing: 'cubic-bezier(0.2, 0, 0, 1)',
} as const;

/**
 * Spacing base (px) and control heights (px). MUI's `theme.spacing` stays 8 (= 2 x this 4 px base) so existing layouts
 * do not shift; use `spacingUnit` for fine-grained values inside theme overrides.
 */
export const spacingUnit = 4;
export const controlHeight = { small: 32, medium: 40 } as const;

/** Table density (px) and the strength of the accent tint on hovered / selected rows. */
export const table = { rowHeight: 36, rowHeightComfortable: 48, hoverAlpha: 0.06, selectedAlpha: 0.12 } as const;

/** Sidebar: selected item sits on `accentSoft` with an accent marker of this width (px). */
export const sidebar = { markerWidth: 3 } as const;

export const layout = { sidebarWidth: 248, topBarHeight: 60 } as const;

/** Starting colours offered for an account's colour bar; the chosen value is user data stored with the account. */
export const accountColors = ['#5b8def', '#16a34a', '#b45309', '#a855f7', '#0891b2', '#dc2626'] as const;

/**
 * Colours offered for a basket's dot (the prototype's palette; the first three are the seeded baskets' colours). A basket
 * can also take any `#rrggbb`; the chosen value is user data stored with the basket.
 */
export const basketColors = [
  { name: 'Indigo', value: '#6366f1' },
  { name: 'Emerald', value: '#10b981' },
  { name: 'Amber', value: '#f59e0b' },
  { name: 'Rose', value: '#f43f5e' },
  { name: 'Sky', value: '#38bdf8' },
  { name: 'Purple', value: '#a855f7' },
  { name: 'Teal', value: '#14b8a6' },
  { name: 'Slate', value: '#64748b' },
] as const;

/**
 * Icon hues (docs/prototype `NAVSTYLE` and `C`). Each hue is a pair: `light` is the icon colour on a dark chip, `strong`
 * the icon colour on a light chip. The chip background is the icon colour at `iconChip.tintLight` / `tintDark` over the
 * surface. Contrast (icon on its chip) is >= 3:1 in both modes; these strong values were darkened minimally from the
 * prototype, keeping the hue:
 *   today  #f97316 -> #df6714 (2.48 -> 3.01)   quality #16a34a -> #159f48 (2.89 -> 3.00)   amber #d97706 -> #d07206 (2.81 -> 3.01)
 * The prototype's `slate` tile hue (#475569) also serves as the navigation fallback (prototype fallback was #64748b).
 */
export const iconHues = {
  today: { light: '#f59e0b', strong: '#df6714' },
  review: { light: '#10b981', strong: '#059669' },
  suggestions: { light: '#d946ef', strong: '#8b5cf6' },
  portfolio: { light: '#38bdf8', strong: '#3b82f6' },
  instruments: { light: '#818cf8', strong: '#4f46e5' },
  baskets: { light: '#fb923c', strong: '#f43f5e' },
  data: { light: '#22d3ee', strong: '#0d9488' },
  quality: { light: '#a3e635', strong: '#159f48' },
  reports: { light: '#c4b5fd', strong: '#7c3aed' },
  lab: { light: '#f472b6', strong: '#db2777' },
  learning: { light: '#2dd4bf', strong: '#6366f1' },
  accounts: { light: '#60a5fa', strong: '#2563eb' },
  services: { light: '#38bdf8', strong: '#6366f1' },
  settings: { light: '#a1a1aa', strong: '#52525b' },
  audit: { light: '#fbbf24', strong: '#b45309' },
  system: { light: '#2dd4bf', strong: '#0f766e' },
  indigo: { light: '#818cf8', strong: '#4f46e5' },
  green: { light: '#34d399', strong: '#059669' },
  amber: { light: '#fbbf24', strong: '#d07206' },
  rose: { light: '#fb7185', strong: '#e11d48' },
  sky: { light: '#38bdf8', strong: '#0284c7' },
  violet: { light: '#c084fc', strong: '#7c3aed' },
  teal: { light: '#2dd4bf', strong: '#0d9488' },
  pink: { light: '#f472b6', strong: '#db2777' },
  slate: { light: '#94a3b8', strong: '#475569' },
} as const satisfies Record<string, { light: string; strong: string }>;

export type IconHueName = keyof typeof iconHues;

/** Chip anatomy: box and icon size (px), corner radius (px). `sm` = nav / header, `md` = stat tile, `lg` = feature card. */
export const iconChip = {
  sm: { box: 28, icon: 17, radius: 8 },
  md: { box: 34, icon: 17, radius: 10 },
  lg: { box: 40, icon: 22, radius: 12 },
  tintLight: 0.12,
  tintDark: 0.16,
} as const;

/** Navigation route path -> hue; unknown routes use `slate`. */
export const navHue: Record<string, IconHueName> = {
  '/': 'today',
  '/review': 'review',
  '/suggestions': 'suggestions',
  '/portfolio': 'portfolio',
  '/instruments': 'instruments',
  '/baskets': 'baskets',
  '/data': 'data',
  '/quality': 'quality',
  '/learning': 'learning',
  '/reports': 'reports',
  '/lab': 'lab',
  '/accounts': 'accounts',
  '/services': 'services',
  '/settings': 'settings',
  '/audit': 'audit',
  '/system': 'system',
};
