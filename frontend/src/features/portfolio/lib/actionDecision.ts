import type { ActionItem, ActionList } from '@/shared/api';
import { stateOf, type ActionStatus } from './actions';

/** What the owner can decide about an item. */
export type Decision = 'snooze' | 'done' | 'reopen';

/** The state an item has once the decision is made. */
export const stateAfter: Record<Decision, ActionStatus> = { snooze: 'snoozed', done: 'done', reopen: 'open' };

/** Days a snoozed item stays hidden: fixed by the API (ADR 0022). */
export const snoozeDays = 7;

/** A calendar day (yyyy-MM-dd) some days later; calendar arithmetic, no time zone involved. */
export function addDays(day: string, days: number): string {
  const date = new Date(`${day}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}

const bump = (counts: Record<string, number>, key: string, by: number): Record<string, number> => ({
  ...counts,
  [key]: Math.max(0, (counts[key] ?? 0) + by),
});

/**
 * The list as it will be once the server has taken the decision, so the screen can show it at once. The counts of the
 * whole scope move with the item (the per-rule and per-severity counts are those of the open items). The list of the
 * item's new state keeps it (changed); the other lists drop it. The server's answer replaces all of this afterwards.
 * `today` is the India day, for the date a snoozed item comes back.
 */
export function applyDecision(
  list: ActionList,
  listStatus: ActionStatus | 'all',
  id: string,
  decision: Decision,
  today: string,
): ActionList {
  const item = list.items.find((candidate) => candidate.id === id);
  if (!item) return list;
  const from = stateOf(item);
  const to = stateAfter[decision];
  if (from === to) return list;

  const changed: ActionItem = {
    ...item,
    state: to,
    snoozedUntil: to === 'snoozed' ? addDays(today, snoozeDays) : null,
  };
  const items = list.items.flatMap((candidate) => {
    if (candidate.id !== id) return [candidate];
    return listStatus === 'all' || listStatus === to ? [changed] : [];
  });

  let { byRule, bySeverity } = list.counts;
  if (from === 'open') {
    byRule = bump(byRule, item.rule, -1);
    bySeverity = bump(bySeverity, item.severity, -1);
  }
  if (to === 'open') {
    byRule = bump(byRule, item.rule, 1);
    bySeverity = bump(bySeverity, item.severity, 1);
  }
  return {
    items,
    counts: {
      ...list.counts,
      [from]: Math.max(0, list.counts[from] - 1),
      [to]: list.counts[to] + 1,
      byRule,
      bySeverity,
    },
  };
}
