import type { ServicesTimeline } from '@/shared/api';
import { istMinutesOfDay } from '@/shared/lib/istClock';

const minutesOf = (time: string): number => {
  const [hours, minutes] = time.split(':').map(Number);
  return (hours ?? 0) * 60 + (minutes ?? 0);
};

/**
 * Is the market open at a moment (ms)? The India wall clock decides the time of day, the timeline of that India day
 * says whether it is a trading day and when the session runs (the calendar, so holidays and special sessions need no
 * rule here). The close is inclusive: the last quote runs at 15:30. Unknown (no timeline yet or it failed) is closed.
 */
export function isMarketOpen(now: number, timeline: ServicesTimeline | undefined): boolean {
  if (!timeline?.tradingDay || !timeline.marketOpen || !timeline.marketClose) return false;
  const minutes = istMinutesOfDay(now);
  return minutes >= minutesOf(timeline.marketOpen) && minutes <= minutesOf(timeline.marketClose);
}

/** The live feed starts this long before the open: prices of the call auction (pre-open) are indicative (ADR 0024 §4). */
export const preOpenMinutes = 15;

export type MarketPhase = 'closed' | 'preopen' | 'open';

/**
 * Where the India clock is in the session of the timeline's day: `preopen` from 15 minutes before the open (09:00) to the
 * open, `open` from the open to the close (inclusive, as `isMarketOpen`), `closed` otherwise and on a day without a session.
 * The live hub is wanted in the first two.
 */
export function marketPhase(now: number, timeline: ServicesTimeline | undefined): MarketPhase {
  if (isMarketOpen(now, timeline)) return 'open';
  if (!timeline?.tradingDay || !timeline.marketOpen) return 'closed';
  const minutes = istMinutesOfDay(now);
  const open = minutesOf(timeline.marketOpen);
  return minutes >= open - preOpenMinutes && minutes < open ? 'preopen' : 'closed';
}
