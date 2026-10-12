import type { CalendarDayInput } from '@/shared/api';
import { maxNoteLength, regularSession } from './calendar';

export type CalendarKind = 'holiday' | 'special';

export interface CalendarForm {
  /** yyyy-MM-dd, '' until chosen. */
  date: string;
  kind: CalendarKind;
  /** HH:mm, used for a special session only. */
  sessionFrom: string;
  sessionTo: string;
  note: string;
}

export const emptyCalendarForm: CalendarForm = {
  date: '',
  kind: 'holiday',
  sessionFrom: regularSession.from,
  sessionTo: regularSession.to,
  note: '',
};

/** Client-side checks (the server checks again and adds the weekend rule); messages by field. */
export function validateCalendarForm(form: CalendarForm): Record<string, string> {
  const errors: Record<string, string> = {};
  if (form.date === '') errors.date = 'Choose a date.';
  if (form.kind === 'special') {
    if (form.sessionFrom === '') errors.sessionFrom = 'Enter the start time.';
    if (form.sessionTo === '') errors.sessionTo = 'Enter the end time.';
    if (form.sessionFrom !== '' && form.sessionTo !== '' && form.sessionFrom >= form.sessionTo) {
      errors.sessionTo = 'The session must end after it starts.';
    }
  }
  if (form.note.length > maxNoteLength) errors.note = `The note can have at most ${String(maxNoteLength)} characters.`;
  return errors;
}

/** The request body: the window only for a special session, the note only when written. */
export function toCalendarInput(form: CalendarForm): CalendarDayInput {
  const note = form.note.trim();
  return {
    date: form.date,
    kind: form.kind,
    ...(form.kind === 'special' ? { sessionFrom: form.sessionFrom, sessionTo: form.sessionTo } : {}),
    ...(note === '' ? {} : { note }),
  };
}
