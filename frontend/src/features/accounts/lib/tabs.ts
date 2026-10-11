export const accountTabs = [
  { key: 'overview', label: 'Overview' },
  { key: 'roles', label: 'Roles' },
  { key: 'trading', label: 'Trading' },
  { key: 'portfolio', label: 'Portfolio' },
  { key: 'data', label: 'Data' },
  { key: 'connection', label: 'Connection' },
  { key: 'activity', label: 'Activity' },
] as const;

export type AccountTab = (typeof accountTabs)[number]['key'];

export const parseTab = (value: string | null): AccountTab =>
  accountTabs.find((tab) => tab.key === value)?.key ?? 'overview';
