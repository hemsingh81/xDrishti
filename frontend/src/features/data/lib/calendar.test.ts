import { ApiError } from '@/shared/api';
import { buildCalendarDay, buildSpecialDay } from '@/test/fixtures';
import { calendarSourceLabel, dayName, holidayLine, kindLabel, removeVerb, sessionText } from './calendar';
import { emptyCalendarForm, toCalendarInput, validateCalendarForm } from './calendarForm';
import { calendarErrorText, fieldErrors } from './errors';

describe('calendar labels', () => {
  it('names a day with its weekday and the holiday with its note', () => {
    expect(dayName({ date: '2026-10-20' })).toBe('Tue 20 Oct 2026');
    expect(holidayLine(buildCalendarDay())).toBe('Tue 20 Oct 2026 — Diwali Laxmi Pujan');
    expect(holidayLine(buildCalendarDay({ note: null }))).toBe('Tue 20 Oct 2026');
  });

  it('says the kind, source and session window in words', () => {
    expect(kindLabel({ kind: 'holiday' })).toBe('Holiday');
    expect(kindLabel({ kind: 'special' })).toBe('Special session');
    expect(calendarSourceLabel('manual')).toBe('You');
    expect(calendarSourceLabel('observed')).toBe('Stored data');
    expect(calendarSourceLabel('nse')).toBe('NSE');
    expect(sessionText(buildSpecialDay())).toBe('18:00–19:00');
    expect(sessionText(buildCalendarDay())).toBe('—');
  });

  it('removes the owner’s own days and dismisses all others', () => {
    expect(removeVerb({ source: 'manual' })).toBe('Remove');
    expect(removeVerb({ source: 'observed' })).toBe('Dismiss');
    expect(removeVerb({ source: 'nse' })).toBe('Dismiss');
  });
});

describe('calendar form', () => {
  it('starts as a holiday with the regular window ready for a special session', () => {
    expect(emptyCalendarForm).toEqual({
      date: '',
      kind: 'holiday',
      sessionFrom: '09:15',
      sessionTo: '15:30',
      note: '',
    });
  });

  it('asks for a date, a window that ends after it starts (special only) and a note within 200 characters', () => {
    expect(validateCalendarForm(emptyCalendarForm)).toEqual({ date: 'Choose a date.' });
    const special = { ...emptyCalendarForm, date: '2026-11-14', kind: 'special' as const };
    expect(validateCalendarForm(special)).toEqual({});
    expect(validateCalendarForm({ ...special, sessionFrom: '', sessionTo: '' })).toEqual({
      sessionFrom: 'Enter the start time.',
      sessionTo: 'Enter the end time.',
    });
    expect(validateCalendarForm({ ...special, sessionFrom: '15:30', sessionTo: '15:30' }).sessionTo).toBe(
      'The session must end after it starts.',
    );
    // A holiday ignores the window.
    expect(
      validateCalendarForm({ ...emptyCalendarForm, date: '2026-12-31', sessionFrom: '16:00', sessionTo: '10:00' }),
    ).toEqual({});
    expect(validateCalendarForm({ ...emptyCalendarForm, date: '2026-12-31', note: 'x'.repeat(201) }).note).toMatch(
      /at most 200/,
    );
  });

  it('sends the window for a special session only, and the trimmed note only when written', () => {
    expect(toCalendarInput({ ...emptyCalendarForm, date: '2026-12-31', note: '  ' })).toEqual({
      date: '2026-12-31',
      kind: 'holiday',
    });
    expect(toCalendarInput({ ...emptyCalendarForm, date: '2026-11-14', kind: 'special', note: ' Muhurat ' })).toEqual({
      date: '2026-11-14',
      kind: 'special',
      sessionFrom: '09:15',
      sessionTo: '15:30',
      note: 'Muhurat',
    });
  });
});

describe('calendar errors', () => {
  const invalid = new ApiError(400, {
    title: 'Not valid',
    code: 'data.invalid',
    errors: { date: ['A.', 'B.'], note: ['C.'] },
  });

  it('joins the messages of each field', () => {
    expect(fieldErrors(invalid)).toEqual({ date: 'A. B.', note: 'C.' });
    expect(fieldErrors(new Error('x'))).toEqual({});
    expect(fieldErrors(new ApiError(500, { title: 'Down' }))).toEqual({});
  });

  it('says a missing day plainly, otherwise the problem title or the connection', () => {
    expect(calendarErrorText(new ApiError(404, { title: 'x', code: 'data.calendar_day_not_found' }), 'f')).toBe(
      'That day is no longer in the calendar. The list was reloaded.',
    );
    expect(calendarErrorText(new ApiError(500, { title: 'Down' }), 'f')).toBe('Down');
    expect(calendarErrorText(new TypeError('network'), 'f')).toMatch(/Could not reach the server/);
  });
});
