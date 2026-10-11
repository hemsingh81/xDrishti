import type { AccountSummary } from '@/shared/api';
import { dhanApp } from './dhanApp';

/** "Connected to Dhan as A. Sharma" (or without the name when the broker did not report one). */
export function connectedMessage(account: Pick<AccountSummary, 'connection'>): string {
  const name = account.connection.brokerUserName;
  return name ? `Connected to ${dhanApp.name} as ${name}.` : `Connected to ${dhanApp.name}.`;
}
