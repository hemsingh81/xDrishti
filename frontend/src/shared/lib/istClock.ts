/**
 * India Standard Time readouts. Always `Asia/Kolkata` through Intl, never the viewer's zone and never `Date`'s local
 * getters, so the clock is the same on a laptop set to London time.
 */
const clock = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'Asia/Kolkata',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hourCycle: 'h23',
});
const day = new Intl.DateTimeFormat('en-IN', {
  timeZone: 'Asia/Kolkata',
  weekday: 'long',
  day: 'numeric',
  month: 'long',
  year: 'numeric',
});

export interface IstClockParts {
  /** Two digits each, e.g. "09", "05", "07". */
  hours: string;
  minutes: string;
  seconds: string;
}

/** The time of day in India at a moment (ms), as two-digit parts. */
export function istClockParts(ms: number): IstClockParts {
  const parts = clock.formatToParts(ms);
  const part = (type: Intl.DateTimeFormatPartTypes) => parts.find((p) => p.type === type)?.value ?? '00';
  return { hours: part('hour'), minutes: part('minute'), seconds: part('second') };
}

/** "Tuesday, 13 October 2026": the calendar day in India at a moment (ms). */
export const istDayLabel = (ms: number): string => day.format(ms);

/** Minutes after midnight in India at a moment (ms). */
export function istMinutesOfDay(ms: number): number {
  const { hours, minutes } = istClockParts(ms);
  return Number(hours) * 60 + Number(minutes);
}
