/** A calendar day (yyyy-MM-dd) plus whole days, as a calendar day; the moment of day plays no part. */
export function addDays(day: string, days: number): string {
  const ms = Date.parse(`${day}T00:00:00Z`) + days * 86_400_000;
  return new Date(ms).toISOString().slice(0, 10);
}
