import { ThemeProvider } from '@mui/material/styles';
import { render } from '@testing-library/react';
import type { ReactElement } from 'react';
import { createAppTheme, palettes, type ColorMode } from '@/theme';
import type { MetricState } from '@/shared/analytics/metric';

/** Renders in the real theme, without the app's providers: the kit needs only the theme. */
export function renderKit(ui: ReactElement, mode: ColorMode = 'light') {
  return render(<ThemeProvider theme={createAppTheme(mode)}>{ui}</ThemeProvider>);
}

/** One state of every kind, with reasons that hold no digit, so "no digit" can be asserted on the text. */
export const nonValueStates: readonly MetricState[] = [
  { kind: 'insufficient', n: 12, min: 30 },
  { kind: 'unavailable', reason: 'The market regime is not stored yet' },
  { kind: 'not_evaluable', reason: 'It needs a champion model, and none is stored' },
  { kind: 'empty' },
  { kind: 'loading' },
  { kind: 'error', reason: 'The server did not answer' },
];

/** The words each kind prints. */
export const words: Record<Exclude<MetricState['kind'], 'value'>, string> = {
  provisional: 'Provisional',
  insufficient: 'Insufficient data',
  unavailable: 'Not available',
  not_evaluable: 'Not evaluable',
  empty: 'No records yet',
  loading: 'Loading',
  error: 'Could not load',
};

const rgb = (hex: string) => {
  const n = Number.parseInt(hex.slice(1), 16);
  return `rgb(${n >> 16}, ${(n >> 8) & 255}, ${n & 255})`;
};

/** The `good` colour as the browser reports it, to assert that a non-value is never drawn in it. */
export const goodColour = (mode: ColorMode = 'light') => rgb(palettes[mode].good);

/** True when the element or any element inside it is drawn in the `good` colour. */
export function usesGoodColour(root: Element, mode: ColorMode = 'light'): boolean {
  const good = goodColour(mode);
  return [root, ...root.querySelectorAll('*')].some((el) => getComputedStyle(el).color === good);
}
