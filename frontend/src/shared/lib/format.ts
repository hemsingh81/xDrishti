/**
 * Formatting helpers. Intl formatters are created once and reused — constructing them is expensive.
 */
const dateTime = new Intl.DateTimeFormat('en-IN', { dateStyle: 'medium', timeStyle: 'medium' });
const date = new Intl.DateTimeFormat('en-IN', { dateStyle: 'medium' });
// Calendar dates (yyyy-MM-dd) carry no time zone: format them as written, whatever the viewer's zone.
const calendarDate = new Intl.DateTimeFormat('en-IN', { dateStyle: 'medium', timeZone: 'UTC' });
const isoDay = /^\d{4}-\d{2}-\d{2}$/;
const decimal = new Intl.NumberFormat('en-IN', { maximumFractionDigits: 2 });
const rupees = new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 0 });
const relative = new Intl.RelativeTimeFormat('en', { numeric: 'auto' });
// Broker token expiries are defined in India time (Dhan: 24 h after sign-in), so they are shown in IST whatever the viewer's zone.
const istTimeAndDay = new Intl.DateTimeFormat('en-IN', {
  timeZone: 'Asia/Kolkata',
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
  day: 'numeric',
  month: 'short',
});

export function formatDateTime(value: string | Date): string {
  return dateTime.format(typeof value === 'string' ? new Date(value) : value);
}

/** "11 Oct 2026": a calendar date (yyyy-MM-dd) as written, or the viewer's day of a timestamp. */
export function formatDate(value: string): string {
  return isoDay.test(value) ? calendarDate.format(new Date(`${value}T00:00:00Z`)) : date.format(new Date(value));
}

export function formatNumber(value: number): string {
  return decimal.format(value);
}

/** Indian rupees with lakh/crore grouping: 200000 -> "₹2,00,000". */
export function formatInr(value: number): string {
  return rupees.format(value);
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

/** "03:30, 11 Oct" — time and day in India time (IST). */
export function formatIstTimeAndDay(value: string | Date): string {
  const parts = istTimeAndDay.formatToParts(typeof value === 'string' ? new Date(value) : value);
  const part = (type: Intl.DateTimeFormatPartTypes) => parts.find((p) => p.type === type)?.value ?? '';
  return `${part('hour')}:${part('minute')}, ${part('day')} ${part('month')}`;
}

/** A wait in words, rounded up to the minute: "1 h 20 min", "45 min", "2 h"; under a minute "less than a minute". */
export function formatHoursMinutes(totalSeconds: number): string {
  if (totalSeconds < 60) {
    return 'less than a minute';
  }
  const minutesTotal = Math.ceil(totalSeconds / 60);
  const hours = Math.floor(minutesTotal / 60);
  const minutes = minutesTotal % 60;
  if (hours === 0) return `${minutes} min`;
  return minutes === 0 ? `${hours} h` : `${hours} h ${minutes} min`;
}
