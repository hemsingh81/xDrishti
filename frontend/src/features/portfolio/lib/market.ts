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
