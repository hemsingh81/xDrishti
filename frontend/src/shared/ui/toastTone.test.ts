import { blend, contrast } from '@/test/colour';
import { iconChip, palettes, resolveIconHue, type ColorMode } from '@/theme';
import type { ToastSeverity } from './toastContext';
import { resolveToastTone, toastRole } from './toastTone';

const severities: ToastSeverity[] = ['success', 'info', 'warning', 'error'];

describe.each<ColorMode>(['light', 'dark'])('toast colours in %s mode', (mode) => {
  const p = palettes[mode];

  it.each(severities)('%s: the title and the message are at least 4.5:1 on the card', (severity) => {
    const { soft } = resolveToastTone(severity, mode);
    expect(contrast(p.text, soft)).toBeGreaterThanOrEqual(4.5);
    expect(contrast(p.textSecondary, soft)).toBeGreaterThanOrEqual(4.5);
  });

  it.each(severities)('%s: the buttons (the severity colour as text) are at least 4.5:1 on the card', (severity) => {
    const { color, soft } = resolveToastTone(severity, mode);
    expect(contrast(color, soft)).toBeGreaterThanOrEqual(4.5);
  });

  it.each(severities)('%s: the accent bar and close button stand out (3:1) from the card and the page', (severity) => {
    const { color, soft } = resolveToastTone(severity, mode);
    for (const behind of [soft, p.surface, p.background]) {
      expect(contrast(color, behind)).toBeGreaterThanOrEqual(3);
    }
  });

  it.each(severities)(
    '%s: the icon is at least 3:1 on its chip (which sits on the surface, not the tint)',
    (severity) => {
      const { hue } = resolveToastTone(severity, mode);
      const { color } = resolveIconHue(hue, mode);
      const tint = mode === 'dark' ? iconChip.tintDark : iconChip.tintLight;
      expect(contrast(color, blend(color, p.surface, tint))).toBeGreaterThanOrEqual(3);
    },
  );

  it('keeps the four severities apart (a card is never mistaken for another by colour)', () => {
    const colours = severities.map((severity) => resolveToastTone(severity, mode).color);
    const surfaces = severities.map((severity) => resolveToastTone(severity, mode).soft);
    expect(new Set(colours).size).toBe(4);
    expect(new Set(surfaces).size).toBe(4);
  });
});

describe('toast tone', () => {
  it('uses the existing hues: success green, info indigo, warning amber, error rose', () => {
    expect(severities.map((severity) => resolveToastTone(severity, 'light').hue)).toEqual([
      'green',
      'indigo',
      'amber',
      'rose',
    ]);
  });

  it('takes the colours from the palette tokens (info is the accent, as in the banners)', () => {
    expect(resolveToastTone('info', 'dark')).toMatchObject({
      color: palettes.dark.accent,
      soft: palettes.dark.accentSoft,
    });
    expect(resolveToastTone('error', 'light')).toMatchObject({
      color: palettes.light.bad,
      soft: palettes.light.badSoft,
    });
  });

  it('interrupts for errors and warnings, waits for a pause otherwise', () => {
    expect(severities.map(toastRole)).toEqual(['status', 'status', 'alert', 'alert']);
  });
});
