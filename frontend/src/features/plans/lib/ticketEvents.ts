import type { components } from '@/shared/api/schema';
import { formatIstDate, formatIstDayMonth, formatWeekdayDate } from '@/shared/lib/format';

type Calendar = components['schemas']['ResultsCalendarView'];
type EventFilter = components['schemas']['EventFilterView'];

/** Why an instrument has no usable results event (the closed reasons of ADR 0182), in words. */
const unavailableWords: Readonly<Record<string, string>> = {
  no_entry: 'no calendar entry',
  only_cancelled: 'only cancelled meetings in the calendar',
  no_upcoming_entry: 'the last meeting has passed and the next is not announced',
};

export interface TicketEvents {
  /** The calendar fact, labelled with when the calendar was read. Never a number for an unknown. */
  results: string;
  /** Only while the filter is ON and readable: what it would do today. Never what the plan decided (the plan view has no such field). */
  filter: string | null;
  /** True when the calendar says nothing certain about the symbol. */
  unknown: boolean;
}

const sessions = (days: number) =>
  days === 0 ? 'results today' : `results in ${String(days)} ${days === 1 ? 'session' : 'sessions'}`;

export interface TicketEventsInput {
  symbol: string;
  calendar: Calendar;
  /** Undefined while it is loading or after it failed (`filterFailed` says which). */
  filter: EventFilter | undefined;
  filterFailed: boolean;
  /** A closed reason for an unavailable event, when the caller has one (the calendar list itself cannot tell them apart). */
  reason?: keyof typeof unavailableWords;
  /** The owner listed `event` in OptionalFilters: an unavailable event stays a printed caveat instead of holding the candidate. */
  optionalCaveat?: boolean;
}

/**
 * What the owner reads under a ticket about the results event of its instrument: the calendar fact as of the moment the
 * calendar was read, and, while the event filter is ON, what it would do today. A symbol the list does not hold (the list is
 * cut at its range, its cap, or the instrument is not in it) is "not known from the calendar list", never "no meeting".
 */
export function ticketEvents({
  symbol,
  calendar,
  filter,
  filterFailed,
  reason,
  optionalCaveat,
}: TicketEventsInput): TicketEvents {
  const asOf = formatIstDayMonth(calendar.asOf);
  const meeting = calendar.meetings.find((m) => m.symbol === symbol);
  const noEntry = calendar.instrumentsWithoutEntry.symbols.includes(symbol);
  let results: string;
  if (meeting) {
    results = `${sessions(meeting.days)} as of ${asOf} (${formatWeekdayDate(meeting.eventSession)}, intimated ${formatIstDate(meeting.intimatedAt)})`;
  } else if (noEntry || reason) {
    results = `unavailable — ${unavailableWords[reason ?? 'no_entry'] ?? 'no calendar entry'} (as of ${asOf})`;
  } else {
    results = `unavailable — not known from the calendar list (as of ${asOf})`;
  }
  const unknown = !meeting;
  let said: string | null = null;
  if (filterFailed) said = 'event filter state unavailable';
  else if (filter?.enabled) {
    if (meeting) {
      said =
        meeting.days <= filter.resultsWithinDays
          ? `the filter would exclude it today (N=${String(filter.resultsWithinDays)})`
          : null;
    } else {
      said = optionalCaveat
        ? 'caveat: event unavailable (the filter does not hold it back; you listed event as optional)'
        : 'the filter would hold it (event unavailable)';
    }
  }
  return { results, filter: said, unknown };
}

/** The plan is for the current session or the next one (up to a weekend away): older and later plans do not get the line. */
export function isNearSession(forDate: string, today: string): boolean {
  const days = (Date.parse(`${forDate}T00:00:00Z`) - Date.parse(`${today}T00:00:00Z`)) / 86_400_000;
  return days >= 0 && days <= 3;
}
