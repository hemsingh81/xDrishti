/** True when a row appended after `before` (the newest id read before the change) was stored as a loosening: the server's direction, no rule of ours. */
export const loosenedSince = (before: number, rows: readonly { id: number; direction: string }[]): boolean =>
  rows.some((r) => r.id > before && r.direction === 'loosen');

/** A number typed in a field; null for an empty or non-numeric entry. */
export const parseNumber = (text: string): number | null => {
  const trimmed = text.trim();
  if (trimmed === '') return null;
  const n = Number(trimmed);
  return Number.isFinite(n) ? n : null;
};

/** True when the typed number is a number inside the inclusive range. */
export const inRange = (text: string, min: number, max: number): boolean => {
  const n = parseNumber(text);
  return n !== null && n >= min && n <= max;
};
