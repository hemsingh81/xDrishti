import type { CalendarDay } from '@/shared/api';
import { formatWeekdayDate } from '@/shared/lib/format';

/** The regular NSE cash session (India time), the default window of a special session. */
export const regularSession = { from: '09:15', to: '15:30' } as const;

export const maxNoteLength = 200;

export const isSpecial = (day: CalendarDay): boolean => day.kind === 'special';

export const kindLabel = (day: Pick<CalendarDay, 'kind'>): string =>
  day.kind === 'special' ? 'Special session' : 'Holiday';

const sourceLabels: Readonly<Record<string, string>> = { manual: 'You', observed: 'Stored data', nse: 'NSE' };

export const calendarSourceLabel = (source: string): string => sourceLabels[source] ?? source;

/** "09:15–15:30" for a special session, "—" for a holiday (no session). */
export function sessionText(day: Pick<CalendarDay, 'sessionFrom' | 'sessionTo'>): string {
  return day.sessionFrom && day.sessionTo ? `${day.sessionFrom}–${day.sessionTo}` : '—';
}

/** "Tue 20 Oct 2026": how a day is named in buttons, dialogs and toasts. */
export const dayName = (day: Pick<CalendarDay, 'date'>): string => formatWeekdayDate(day.date);

/**
 * What the delete does (API semantics): the owner's own day is removed; a day from NSE's list or a proposal from the
 * stored bars is kept as dismissed so that it is not brought back.
 */
export const removeVerb = (day: Pick<CalendarDay, 'source'>): 'Remove' | 'Dismiss' =>
  day.source === 'manual' ? 'Remove' : 'Dismiss';

/** "Tue 20 Oct 2026 — Diwali": the next holiday as the strip says it. */
export const holidayLine = (day: Pick<CalendarDay, 'date' | 'note'>): string =>
  day.note ? `${dayName(day)} — ${day.note}` : dayName(day);

/**
 * The sentence under a proposal that lies next to a confirmed day (`replaces` names that day's kind): the stored bars
 * contradict the day the calendar lists. The confirmed day stays in force until the owner decides.
 */
export function replacesText(day: Pick<CalendarDay, 'date' | 'replaces'>): string | null {
  if (day.replaces === 'holiday') {
    return `A session was seen on ${dayName(day)}, which is listed as a holiday. Confirm to treat it as a special session.`;
  }
  if (day.replaces === 'special') {
    return `No session was seen on ${dayName(day)}, which is listed as a special session. Confirm to treat it as a holiday.`;
  }
  return null;
}
