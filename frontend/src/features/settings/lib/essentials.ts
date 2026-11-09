/** The five Essential settings in the order the owner sees them, with a plain sentence each (ADR 0242). `to` is the screen that already reads and changes the setting. */
export const essentialSettings = [
  { key: 'auto.enabled', help: 'Lets the system carry out its routine work for you. It never places an order.' },
  { key: 'suggest.scope', help: 'Which instruments the system may suggest.' },
  {
    key: 'risk.profile',
    help: 'How much risk each trade may take.',
    to: '/settings?tab=profiles',
    linkLabel: 'Open risk profiles',
  },
  {
    key: 'chargesheet.gate',
    help: 'Whether you confirm the charge sheet before it is used.',
    to: '/settings?tab=charge-sheet',
    linkLabel: 'Open the charge sheet',
  },
  { key: 'notices.failure', help: 'Tell me when a service run fails.' },
] as const;

/** On and Off for a switch, the stored words otherwise. */
export const plainValue = (value: string | null): string | null =>
  value === 'true' ? 'On' : value === 'false' ? 'Off' : value;
