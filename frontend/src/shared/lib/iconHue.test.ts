import { hueForLabel } from './iconHue';

describe('hueForLabel', () => {
  it.each([
    ['Holdings value', 'indigo'],
    ['Unrealised', 'green'],
    ['Day change', 'sky'],
    ['Funds available', 'amber'],
    ['Max drawdown', 'rose'],
  ])('maps %s to %s', (label, hue) => {
    expect(hueForLabel(label)).toBe(hue);
  });

  it.each(['Setup', 'Backup', 'Download', 'Breakdown', 'Calibrate', 'Operate', 'Metadata'])(
    'does not match a keyword inside "%s"',
    (label) => {
      expect(hueForLabel(label, 0)).toBe('indigo');
    },
  );

  it('matches whole words and lets warn win over the broader rules', () => {
    expect(hueForLabel('Link is down')).toBe('rose');
    expect(hueForLabel('Services up')).toBe('green');
    expect(hueForLabel('Warning: stale data')).toBe('amber');
    expect(hueForLabel('Hit rate')).toBe('violet');
  });

  it('rotates unmatched labels by index', () => {
    expect(hueForLabel('zzz', 0)).not.toBe(hueForLabel('zzz', 1));
  });
});
