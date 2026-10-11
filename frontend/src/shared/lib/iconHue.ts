import type { IconHueName } from '@/theme';

/**
 * Keyword -> hue rules for stat tiles; first match wins, so the specific `warn` rule precedes the broader ones. The set
 * is intentionally smaller than the prototype's `TILE_RULES`: restore a prototype rule only when a current tile needs
 * it. Short words are anchored (`\b`) so "setup", "backup", "download", "calibrate" or "operate" do not match.
 */
const TILE_HUE_RULES: readonly (readonly [RegExp, IconHueName])[] = [
  [/warn|stale|expir/, 'amber'],
  [/fail|drawdown|loss|reject|error|\bdown\b/, 'rose'],
  [/pass|healthy|\bok\b|\bup\b|connected/, 'green'],
  [/xirr|return|%|\brate\b/, 'violet'],
  [/p&l|pnl|profit|\bgain|realised/, 'green'],
  [/\bday\b|\btime\b|updated|checked|since/, 'sky'],
  [/value|worth|holdings/, 'indigo'],
  [/invested|cost|fund|capital|cash/, 'amber'],
  [/model|learn|lesson|version|\bdata\b|import|\bbars\b/, 'teal'],
  [/\bauto/, 'pink'],
  [/service|account|broker/, 'sky'],
  [/risk|limit/, 'rose'],
];

const ROTATION: readonly IconHueName[] = ['indigo', 'teal', 'amber', 'violet', 'sky', 'pink', 'green', 'rose'];

/** Hue for a tile label; unmatched labels rotate by `index` so neighbouring tiles differ. */
export function hueForLabel(label: string, index = 0): IconHueName {
  const text = label.toLowerCase();
  for (const [re, hue] of TILE_HUE_RULES) {
    if (re.test(text)) return hue;
  }
  return ROTATION[index % ROTATION.length] ?? 'indigo';
}
