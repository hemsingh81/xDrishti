/** The words the owner reads for the live capture (spec P11-01). The API's codes are the keys; a code not listed is shown as it is. */
export const healthLabels: Readonly<Record<string, { label: string; tone: 'success' | 'warning' | 'default' }>> = {
  healthy: { label: 'Healthy', tone: 'success' },
  gap: { label: 'Gap', tone: 'warning' },
  not_run: { label: 'Not running', tone: 'default' },
};

export const outcomeLabels: Readonly<
  Record<string, { label: string; tone: 'success' | 'warning' | 'error' | 'default' }>
> = {
  filled: { label: 'Filled', tone: 'success' },
  partial: { label: 'Partly filled', tone: 'warning' },
  over_allocation: { label: 'Over the allocation', tone: 'warning' },
  not_triggered: { label: 'Not triggered', tone: 'default' },
  missed_no_order: { label: 'Missed: no order placed', tone: 'error' },
  missed_unfilled: { label: 'Missed: order not filled', tone: 'error' },
  rejected: { label: 'Rejected', tone: 'error' },
  missed_unknown: { label: 'Missed: order evidence unavailable', tone: 'warning' },
  unknown: { label: 'Unknown', tone: 'default' },
};

export const flagLabels: Readonly<Record<string, string>> = {
  over_allocation: 'Over the allocation',
  size_below_allocation: 'Below the allocation',
  chased_entry: 'Chased entry',
  unprotected: 'Unprotected',
};

export const protectionLabels: Readonly<Record<string, string>> = {
  protected: 'Protected by a stop',
  unprotected: 'Unprotected',
  protection_unknown: 'Unknown (forever orders are not read)',
  not_applicable: 'No position',
  unavailable: 'Unavailable',
};

export const readOnlySentence =
  'Read only: this tab shows what you did in Dhan today. It places, changes and cancels nothing.';
