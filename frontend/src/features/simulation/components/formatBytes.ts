const units = ['B', 'KB', 'MB', 'GB', 'TB'];

/** Decimal bytes as text (1 GB = 1000 MB), one decimal above bytes. */
export function formatBytes(bytes: number): string {
  let value = bytes;
  let unit = 0;
  while (value >= 1000 && unit < units.length - 1) {
    value /= 1000;
    unit += 1;
  }
  return `${unit === 0 ? String(value) : value.toFixed(1)} ${units[unit] ?? 'B'}`;
}
