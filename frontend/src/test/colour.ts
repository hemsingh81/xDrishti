/** Colour maths for tests that check contrast (WCAG 2.x). Colours are `#rrggbb`. */

function channel(v: number): number {
  const c = v / 255;
  return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
}

function luminance(hex: string): number {
  const n = Number.parseInt(hex.slice(1), 16);
  return 0.2126 * channel((n >> 16) & 255) + 0.7152 * channel((n >> 8) & 255) + 0.0722 * channel(n & 255);
}

/** `fg` at `alpha` painted over `bg`, as #rrggbb. */
export function blend(fg: string, bg: string, alpha: number): string {
  const f = Number.parseInt(fg.slice(1), 16);
  const b = Number.parseInt(bg.slice(1), 16);
  const mix = (shift: number) =>
    Math.round(((f >> shift) & 255) * alpha + ((b >> shift) & 255) * (1 - alpha))
      .toString(16)
      .padStart(2, '0');
  return `#${mix(16)}${mix(8)}${mix(0)}`;
}

/** WCAG 2.x contrast ratio between two #rrggbb colours. */
export function contrast(a: string, b: string): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x) as [number, number];
  return (hi + 0.05) / (lo + 0.05);
}
