import type { ServiceRun, ServiceView } from '@/shared/api';

export const scheduleTypes = ['daily', 'weekly', 'after', 'window', 'interval', 'continuous'] as const;
export type ScheduleType = (typeof scheduleTypes)[number];

export const isScheduleType = (value: string): value is ScheduleType => scheduleTypes.some((t) => t === value);

/** The choices of the type select, in the words of the prototype. */
export const scheduleTypeLabels: Readonly<Record<ScheduleType, string>> = {
  daily: 'Daily at a time',
  weekly: 'Weekly',
  after: 'After another service',
  window: 'Time window (market hours)',
  interval: 'Repeat every N minutes in a window',
  continuous: 'Continuous',
};

export const weekdays = ['mon', 'tue', 'wed', 'thu', 'fri', 'sat', 'sun'] as const;
export type Weekday = (typeof weekdays)[number];

export const isWeekday = (value: string | null | undefined): value is Weekday => weekdays.some((d) => d === value);

export const tradingWeekdays: readonly Weekday[] = ['mon', 'tue', 'wed', 'thu', 'fri'];

export const weekdayShort: Readonly<Record<Weekday, string>> = {
  mon: 'Mon',
  tue: 'Tue',
  wed: 'Wed',
  thu: 'Thu',
  fri: 'Fri',
  sat: 'Sat',
  sun: 'Sun',
};

export const weekdayLong: Readonly<Record<Weekday, string>> = {
  mon: 'Monday',
  tue: 'Tuesday',
  wed: 'Wednesday',
  thu: 'Thursday',
  fri: 'Friday',
  sat: 'Saturday',
  sun: 'Sunday',
};

const triggerLabels: Readonly<Record<string, string>> = {
  scheduled: 'Scheduled',
  catch_up: 'Catch-up',
  after: 'After another service',
  manual: 'Manual',
};

/** "Scheduled" · "Catch-up" · "After another service" · "Manual": who or what started a run. */
export const triggerLabel = (run: Pick<ServiceRun, 'trigger'>): string => triggerLabels[run.trigger] ?? run.trigger;

const runStatusLabels: Readonly<Record<string, string>> = {
  running: 'Running',
  succeeded: 'Succeeded',
  failed: 'Failed',
  skipped: 'Skipped',
};

export const runStatusLabel = (status: string): string => runStatusLabels[status] ?? status;

const statusLabels: Readonly<Record<string, string>> = {
  healthy: 'Healthy',
  running: 'Running',
  failed: 'Failed',
  paused: 'Paused',
};

export const serviceStatusLabel = (status: string): string => statusLabels[status] ?? status;

/** "retries 2 · timeout 60 min · catch-up" under the schedule in words. */
export function executionText(execution: ServiceView['execution']): string {
  const parts = [`retries ${String(execution.retries)}`, `timeout ${String(execution.timeoutMinutes)} min`];
  if (execution.catchUp) parts.push('catch-up');
  return parts.join(' · ');
}
