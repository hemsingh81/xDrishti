/** The words the owner reads for the compliance record (spec P11-05). The API's kinds and outcomes are the keys. */
export const kindLabels: Readonly<Record<string, string>> = {
  static_ip: 'Static IP',
  sebi_recheck: 'SEBI re-check',
  dhan_ip_read_policy: 'Dhan IP rule for reads',
};

export const kinds = ['static_ip', 'sebi_recheck', 'dhan_ip_read_policy'] as const;
export type Kind = (typeof kinds)[number];

/** The outcomes of each kind, as the API allows them, with their words. */
export const outcomesOf: Readonly<Record<Kind, readonly { value: string; label: string }[]>> = {
  static_ip: [
    { value: 'not_needed_for_manual', label: 'Not needed for hand-placed orders' },
    { value: 'requested', label: 'Requested from the ISP' },
    { value: 'obtained', label: 'Obtained' },
    { value: 'whitelisted', label: 'Whitelisted at Dhan' },
    { value: 'declined', label: 'Declined' },
    { value: 'blocked', label: 'Blocked' },
  ],
  sebi_recheck: [
    { value: 'rechecked_no_change', label: 'Re-checked, no change' },
    { value: 'rechecked_changed', label: 'Re-checked, changed' },
    { value: 'blocked', label: 'Blocked' },
  ],
  dhan_ip_read_policy: [
    { value: 'reads_not_ip_restricted', label: 'Reads are not IP-restricted' },
    { value: 'reads_ip_restricted', label: 'Reads are IP-restricted' },
    { value: 'unanswered', label: 'Unanswered' },
  ],
};

export const outcomeLabel = (outcome: string): string =>
  Object.values(outcomesOf)
    .flat()
    .find((o) => o.value === outcome)?.label ?? outcome;

export const staticIpStates = [
  { value: 'none', label: 'None' },
  { value: 'requested', label: 'Requested' },
  { value: 'active', label: 'Active at the ISP' },
  { value: 'whitelisted', label: 'Whitelisted at Dhan' },
] as const;

export const stateLabel = (state: string): string => staticIpStates.find((s) => s.value === state)?.label ?? state;

/** The sentence that says what a record is (criterion 9). */
export const noteSentence =
  'This record is your note of what you were told; the system checks nothing at Dhan or the ISP.';

export const runbookPath = 'docs/research/p11-static-ip-and-sebi-runbook.md';
