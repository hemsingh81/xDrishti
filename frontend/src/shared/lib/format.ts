/**
 * Formatting helpers. Intl formatters are created once and reused — constructing them is expensive.
 */
const dateTime = new Intl.DateTimeFormat('en-IN', { dateStyle: 'medium', timeStyle: 'medium' });
const decimal = new Intl.NumberFormat('en-IN', { maximumFractionDigits: 2 });
const relative = new Intl.RelativeTimeFormat('en', { numeric: 'auto' });

export function formatDateTime(value: string | Date): string {
  return dateTime.format(typeof value === 'string' ? new Date(value) : value);
}

export function formatNumber(value: number): string {
  return decimal.format(value);
}

/** "3 hours 5 minutes" style duration from seconds. */
export function formatDuration(totalSeconds: number): string {
  const s = Math.max(0, Math.floor(totalSeconds));
  const days = Math.floor(s / 86_400);
  const hours = Math.floor((s % 86_400) / 3_600);
  const minutes = Math.floor((s % 3_600) / 60);
  if (days > 0) return `${days}d ${hours}h`;
  if (hours > 0) return `${hours}h ${minutes}m`;
  if (minutes > 0) return `${minutes}m ${s % 60}s`;
  return `${s}s`;
}

/** "12 seconds ago" relative to now. */
export function formatAgo(value: string | Date, now: Date = new Date()): string {
  const seconds = Math.round(((typeof value === 'string' ? new Date(value) : value).getTime() - now.getTime()) / 1000);
  const abs = Math.abs(seconds);
  if (abs < 60) return relative.format(seconds, 'second');
  if (abs < 3_600) return relative.format(Math.round(seconds / 60), 'minute');
  if (abs < 86_400) return relative.format(Math.round(seconds / 3_600), 'hour');
  return relative.format(Math.round(seconds / 86_400), 'day');
}
