const minute = 60_000;

/**
 * How long until a moment, in words and rounded up to the minute: "in 45 min", "in 3 h 12 min", "in 2 d 3 h"; "due now"
 * once it has come (the list is read again a few seconds after a start, so it does not stay that way).
 */
export function untilText(targetMs: number, nowMs: number): string {
  const minutes = Math.ceil((targetMs - nowMs) / minute);
  if (minutes <= 0) return 'due now';
  if (minutes < 60) return `in ${String(minutes)} min`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) {
    const rest = minutes % 60;
    return rest === 0 ? `in ${String(hours)} h` : `in ${String(hours)} h ${String(rest)} min`;
  }
  const days = Math.floor(hours / 24);
  const restHours = hours % 24;
  return restHours === 0 ? `in ${String(days)} d` : `in ${String(days)} d ${String(restHours)} h`;
}
