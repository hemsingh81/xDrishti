import type { ServiceIssue, ServiceParameter, ServiceView } from '@/shared/api';

export const isError = (issue: ServiceIssue): boolean => issue.severity === 'error';

/** An issue with the name of the service it is about, for the banner. */
export interface NamedIssue {
  issue: ServiceIssue;
  serviceName: string;
}

const fieldNames: Readonly<Record<string, string>> = {
  'schedule.type': 'Schedule type',
  'schedule.time': 'Time',
  'schedule.days': 'Days',
  'schedule.weekday': 'Day',
  'schedule.after': 'Run after',
  'schedule.from': 'From',
  'schedule.to': 'To',
  'schedule.everyMinutes': 'Every (minutes)',
  schedule: 'Schedule',
  enabled: 'Enabled',
  retries: 'Retries',
  timeoutMinutes: 'Timeout',
  catchUp: 'Catch up',
  notifyOnFailure: 'Notify on failure',
};

/** The label of a field path in the words of the dialog ("schedule.time" is "Time"). */
export function fieldName(field: string, parameters: readonly ServiceParameter[] = []): string {
  const known = fieldNames[field];
  if (known) return known;
  const parameter = field.startsWith('parameters.') ? field.slice('parameters.'.length) : null;
  return parameters.find((p) => p.name === parameter)?.label ?? field;
}

const keyOf = (issue: ServiceIssue) =>
  [issue.serviceId, issue.field, issue.code, issue.relatedServiceId ?? '', issue.message].join('|');

/** Every issue of every service, once, with the service's name; errors first, then in the order of the screen. */
export function namedIssues(services: readonly ServiceView[]): NamedIssue[] {
  const names = new Map(services.map((s) => [s.id, s.name]));
  const seen = new Set<string>();
  const all: NamedIssue[] = [];
  for (const service of services) {
    for (const issue of service.issues) {
      const key = keyOf(issue);
      if (seen.has(key)) continue;
      seen.add(key);
      all.push({ issue, serviceName: names.get(issue.serviceId) ?? service.name });
    }
  }
  return [...all.filter((n) => isError(n.issue)), ...all.filter((n) => !isError(n.issue))];
}

const typeFields: Readonly<Record<string, readonly string[]>> = {
  daily: ['schedule.time', 'schedule.days'],
  weekly: ['schedule.weekday', 'schedule.time'],
  after: ['schedule.after'],
  window: ['schedule.from', 'schedule.to', 'schedule.days'],
  interval: ['schedule.from', 'schedule.to', 'schedule.everyMinutes', 'schedule.days'],
  continuous: [],
};

/** The fields the dialog shows right now (an error about one is written on it; the others are listed under the preview). */
export function shownFields(type: string, parameters: readonly ServiceParameter[]): ReadonlySet<string> {
  return new Set([
    'schedule.type',
    'retries',
    'timeoutMinutes',
    ...(typeFields[type] ?? []),
    ...parameters.map((p) => `parameters.${p.name}`),
  ]);
}

const onField = (issue: ServiceIssue, serviceId: string, shown: ReadonlySet<string>) =>
  isError(issue) && issue.serviceId === serviceId && shown.has(issue.field);

/** The errors of the edited service by field, to show on the field itself (several messages joined). */
export function errorsByField(
  issues: readonly ServiceIssue[],
  serviceId: string,
  shown: ReadonlySet<string>,
): Readonly<Record<string, string>> {
  const result: Record<string, string> = {};
  for (const issue of issues) {
    if (!onField(issue, serviceId, shown)) continue;
    result[issue.field] = result[issue.field] ? `${result[issue.field] ?? ''} ${issue.message}` : issue.message;
  }
  return result;
}

/** The issues the list under the preview shows: everything that is not already on a field of the edited service. */
export function listedIssues(
  issues: readonly ServiceIssue[],
  serviceId: string,
  shown: ReadonlySet<string>,
): ServiceIssue[] {
  return issues.filter((issue) => !onField(issue, serviceId, shown));
}

/** The issues of several sources once each (the same field, service and message is one finding). */
export function mergeIssues(...lists: readonly (readonly ServiceIssue[])[]): ServiceIssue[] {
  const seen = new Set<string>();
  return lists.flat().filter((issue) => {
    const key = `${issue.serviceId}|${issue.field}|${issue.severity}|${issue.message}`;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}
