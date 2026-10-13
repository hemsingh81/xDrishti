import type { ScheduleSpec, ServiceParameter, ServiceScheduleInput, ServiceView } from '@/shared/api';
import { isScheduleType, isWeekday, tradingWeekdays, type ScheduleType, type Weekday } from './labels';

/** The Configure dialog's draft: everything the owner can set, as the inputs hold it (text for numbers and times). */
export interface ScheduleForm {
  enabled: boolean;
  type: ScheduleType;
  time: string;
  days: readonly Weekday[];
  tradingDaysOnly: boolean;
  weekday: Weekday;
  after: string;
  from: string;
  to: string;
  everyMinutes: string;
  retries: string;
  timeoutMinutes: string;
  catchUp: boolean;
  notifyOnFailure: boolean;
  /** By parameter name: the text of a number, the state of a switch. */
  parameters: Readonly<Record<string, string | boolean>>;
}

/** Error text by the API's field path (`schedule.time`, `retries`, `parameters.lookbackDays` …). */
export type FieldErrors = Readonly<Record<string, string>>;

export const retriesRange = { min: 0, max: 5 } as const;
export const timeoutRange = { min: 1, max: 240 } as const;
export const everyRange = { min: 1, max: 120 } as const;

const defaults = { time: '16:00', from: '09:15', to: '15:30', everyMinutes: '5' } as const;

const daysOf = (spec: ScheduleSpec): readonly Weekday[] => {
  const days = (spec.days ?? []).filter(isWeekday);
  return days.length > 0 || !spec.tradingDaysOnly ? days : tradingWeekdays;
};

const textOf = (value: unknown): string =>
  typeof value === 'number' || typeof value === 'string' ? String(value) : '';

/** The draft a service starts the dialog with: its stored schedule, execution settings and parameter values. */
export function formOfService(service: ServiceView): ScheduleForm {
  const spec = service.schedule;
  const type = isScheduleType(spec.type) ? spec.type : (service.allowedScheduleTypes.find(isScheduleType) ?? 'daily');
  return {
    enabled: service.enabled,
    type,
    time: spec.time ?? defaults.time,
    days: spec.days === undefined || spec.days === null ? tradingWeekdays : daysOf(spec),
    tradingDaysOnly: spec.tradingDaysOnly,
    weekday: isWeekday(spec.weekday) ? spec.weekday : 'mon',
    after: spec.after ?? '',
    from: spec.from ?? defaults.from,
    to: spec.to ?? defaults.to,
    everyMinutes: String(spec.everyMinutes ?? defaults.everyMinutes),
    retries: String(service.execution.retries),
    timeoutMinutes: String(service.execution.timeoutMinutes),
    catchUp: service.execution.catchUp,
    notifyOnFailure: service.execution.notifyOnFailure,
    parameters: Object.fromEntries(
      service.parameters.map((p) => [p.name, p.kind === 'boolean' ? p.value === true : textOf(p.value)]),
    ),
  };
}

const timePattern = /^([01]\d|2[0-3]):[0-5]\d$/;
export const isTime = (value: string): boolean => timePattern.test(value);

const wholeNumber = (text: string): number | null => (/^\d+$/.test(text.trim()) ? Number(text.trim()) : null);

/** The days the schedule sends: Monday to Friday while "trading days only" is on (the server uses those days then). */
const daysToSend = (form: ScheduleForm): Weekday[] => [...(form.tradingDaysOnly ? tradingWeekdays : form.days)];

function scheduleOf(form: ScheduleForm): ScheduleSpec {
  const { type, tradingDaysOnly } = form;
  switch (type) {
    case 'daily':
      return { type, time: form.time, days: daysToSend(form), tradingDaysOnly };
    case 'weekly':
      return { type, time: form.time, weekday: form.weekday, tradingDaysOnly };
    case 'after':
      return { type, after: form.after, tradingDaysOnly: false };
    case 'window':
      return { type, from: form.from, to: form.to, days: daysToSend(form), tradingDaysOnly };
    case 'interval':
      return {
        type,
        from: form.from,
        to: form.to,
        everyMinutes: Number(form.everyMinutes),
        days: daysToSend(form),
        tradingDaysOnly,
      };
    case 'continuous':
      return { type, tradingDaysOnly: false };
  }
}

function parameterValue(parameter: ServiceParameter, value: string | boolean | undefined): unknown {
  if (parameter.kind === 'boolean') return value === true;
  return Number(typeof value === 'string' ? value : '');
}

/** The request body of a save or preview (the form must be valid: see `formErrors`). */
export function inputOfForm(form: ScheduleForm, service: ServiceView): ServiceScheduleInput {
  const parameters = Object.fromEntries(
    service.parameters.map((p) => [p.name, parameterValue(p, form.parameters[p.name])]),
  );
  return {
    enabled: form.enabled,
    schedule: scheduleOf(form),
    retries: Number(form.retries),
    timeoutMinutes: Number(form.timeoutMinutes),
    catchUp: form.catchUp,
    notifyOnFailure: form.notifyOnFailure,
    // The generated type of a free-form object is `Record<string, never>`; the values are numbers and booleans.
    parameters: parameters as Record<string, never>,
    revision: service.revision,
  };
}

function parameterError(parameter: ServiceParameter, value: string | boolean | undefined): string | null {
  if (parameter.kind === 'boolean') return null;
  const number = typeof value === 'string' && value.trim() !== '' ? Number(value) : Number.NaN;
  if (!Number.isFinite(number)) return 'Enter a number.';
  if (parameter.kind === 'integer' && !Number.isInteger(number)) return 'Enter a whole number.';
  if (number < parameter.min || number > parameter.max)
    return `Between ${String(parameter.min)} and ${String(parameter.max)}.`;
  return null;
}

/** What is wrong with the draft before it is sent (a missing number, a time that is not a time); the server checks the rest. */
export function formErrors(form: ScheduleForm, service: ServiceView): FieldErrors {
  const errors: Record<string, string> = {};
  const { type } = form;
  if ((type === 'daily' || type === 'weekly') && !isTime(form.time)) errors['schedule.time'] = 'Enter a time.';
  if (type === 'window' || type === 'interval') {
    if (!isTime(form.from)) errors['schedule.from'] = 'Enter a start time.';
    if (!isTime(form.to)) errors['schedule.to'] = 'Enter an end time.';
  }
  if (type === 'interval') {
    const every = wholeNumber(form.everyMinutes);
    if (every === null || every < everyRange.min || every > everyRange.max) {
      errors['schedule.everyMinutes'] = `Between ${String(everyRange.min)} and ${String(everyRange.max)} minutes.`;
    }
  }
  if (['daily', 'window', 'interval'].includes(type) && !form.tradingDaysOnly && form.days.length === 0) {
    errors['schedule.days'] = 'Choose at least one day.';
  }
  if (type === 'after' && form.after === '') errors['schedule.after'] = 'Choose the service to run after.';
  const retries = wholeNumber(form.retries);
  if (retries === null || retries < retriesRange.min || retries > retriesRange.max) {
    errors.retries = `Between ${String(retriesRange.min)} and ${String(retriesRange.max)}.`;
  }
  const timeout = wholeNumber(form.timeoutMinutes);
  if (timeout === null || timeout < timeoutRange.min || timeout > timeoutRange.max) {
    errors.timeoutMinutes = `Between ${String(timeoutRange.min)} and ${String(timeoutRange.max)} minutes.`;
  }
  for (const parameter of service.parameters) {
    const message = parameterError(parameter, form.parameters[parameter.name]);
    if (message) errors[`parameters.${parameter.name}`] = message;
  }
  return errors;
}
