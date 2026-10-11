import type { AccountSummary } from '@/shared/api';
import { dhanApp } from './dhanApp';

export type RoleKey = 'data' | 'trading' | 'portfolio';

export const roleKeys: readonly RoleKey[] = ['data', 'trading', 'portfolio'];

export const roleInfo: Record<
  RoleKey,
  { label: string; chipColor: 'info' | 'success' | 'primary'; description: string }
> = {
  data: {
    label: 'Data',
    chipColor: 'info',
    description:
      'Instrument master, end-of-day 1-minute fetch, live feed for the active set, quotes. Needs a Dhan Data API subscription.',
  },
  trading: {
    label: 'Trading',
    chipColor: 'success',
    description: 'Receives approved plan tickets sized to its capital and risk profile; fills auto-journaled.',
  },
  portfolio: {
    label: 'Portfolio',
    chipColor: 'primary',
    description: 'Holdings, positions, trade history and ledger synced nightly; included in portfolio views and XIRR.',
  },
};

export const modeLabels: Record<string, string> = { paper: 'paper', manual_live: 'manual live' };
export const modeLabel = (mode: string): string => modeLabels[mode] ?? mode;

export const instrumentLabels: Record<string, string> = {
  EQ_MIS: 'EQ_MIS',
  EQ_CNC: 'EQ_CNC',
  FUT: 'FUT',
};

export const weeklyLossActionLabels: Record<string, string> = {
  halve_size: 'Halve position size',
  stop_new_entries: 'Stop new entries',
  alert_only: 'Alert only',
};

/** Roles that are switched on, in display order. */
export const enabledRoles = (account: Pick<AccountSummary, 'roles'>): RoleKey[] =>
  roleKeys.filter((role) => account.roles[role].enabled);

/** Text for the avatar-less row: `Dhan · AB12••34`. */
export const accountSubtitle = (account: Pick<AccountSummary, 'maskedClientId'>): string =>
  `${dhanApp.name} · ${account.maskedClientId}`;

/** "AccountCreated" -> "Account created". */
export function humanizeEvent(event: string): string {
  const words = event.replace(/([a-z0-9])([A-Z])/g, '$1 $2').toLowerCase();
  return words.charAt(0).toUpperCase() + words.slice(1);
}
