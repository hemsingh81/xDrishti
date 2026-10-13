import type { ActionItem } from '@/shared/api';

/** Which items the Action center lists: waiting for a decision, hidden for 7 days, or marked done. */
export const actionStatuses = ['open', 'snoozed', 'done'] as const;
export type ActionStatus = (typeof actionStatuses)[number];

export const actionStatusLabels: Record<ActionStatus, string> = { open: 'Open', snoozed: 'Snoozed', done: 'Done' };

export const severities = ['high', 'medium', 'info'] as const;
export type Severity = (typeof severities)[number];

export const severityLabels: Record<Severity, string> = { high: 'High', medium: 'Medium', info: 'Info' };

export const ruleLabels: Record<string, string> = {
  below_cost: 'Below cost',
  large_gain: 'Large gain',
  overweight: 'Overweight',
  sector_concentration: 'Sector concentration',
  long_term_soon: 'Long term soon',
  lagging_nifty: 'Lagging Nifty',
  not_in_basket: 'Not in a basket',
};

/** The rule in words; a rule this version does not know is shown as the API names it, not hidden. */
export const ruleLabel = (rule: string): string => ruleLabels[rule] ?? rule;

/** A severity this version does not know is shown with the least urgent ones. */
export const severityOf = (item: Pick<ActionItem, 'severity'>): Severity =>
  severities.find((s) => s === item.severity) ?? 'info';

export interface SeverityGroup {
  severity: Severity;
  items: ActionItem[];
}

/** The items by severity, most urgent first, keeping the API's order inside a group; empty groups are left out. */
export function groupBySeverity(items: readonly ActionItem[]): SeverityGroup[] {
  return severities
    .map((severity) => ({ severity, items: items.filter((item) => severityOf(item) === severity) }))
    .filter((group) => group.items.length > 0);
}

const statusOf = (value: string): ActionStatus => actionStatuses.find((s) => s === value) ?? 'open';

/** The state of an item as a status. */
export const stateOf = (item: Pick<ActionItem, 'state'>): ActionStatus => statusOf(item.state);

/** The header's words: "5 need a decision", "1 needs a decision", "Nothing needs a decision". */
export function needDecisionText(open: number): string {
  if (open <= 0) return 'Nothing needs a decision';
  return `${String(open)} ${open === 1 ? 'needs' : 'need'} a decision`;
}

/** Whether the item is about a holding the owner can open (a holding rule, or the largest holding of a sector). */
export const holdingIdOf = (item: ActionItem): string | null => item.holdingId ?? item.numbers.largestHoldingId ?? null;

/** What an item is about, for names and messages: the symbol, or the account and the sector. */
export function subjectOf(item: ActionItem): string {
  if (item.symbol) return item.symbol;
  const sector = item.numbers.sector;
  return sector ? `${item.accountName} ${sector}` : item.accountName;
}

export type RowActionKey = 'open' | 'set_alert' | 'add_to_basket' | 'snooze' | 'done' | 'reopen';

export const rowActionLabels: Record<RowActionKey, string> = {
  open: 'Open',
  set_alert: 'Set alert',
  add_to_basket: 'Add to basket',
  snooze: 'Snooze 7 days',
  done: 'Done',
  reopen: 'Reopen',
};

/**
 * The buttons of an item, in order: what the API suggests, minus what cannot work (an alert or a basket needs an
 * instrument in the master list). Snoozed and done items can be opened and reopened, nothing else.
 */
export function rowActionsOf(item: ActionItem): RowActionKey[] {
  const suggested = new Set(item.suggestions);
  const keys: RowActionKey[] = [];
  if (suggested.has('open')) keys.push('open');
  if (stateOf(item) !== 'open') return [...keys, 'reopen'];
  if (suggested.has('set_alert') && item.instrumentId) keys.push('set_alert');
  if (suggested.has('add_to_basket') && item.instrumentId) keys.push('add_to_basket');
  if (suggested.has('snooze')) keys.push('snooze');
  if (suggested.has('done')) keys.push('done');
  return keys;
}
