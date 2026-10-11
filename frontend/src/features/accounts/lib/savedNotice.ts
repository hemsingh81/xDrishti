import type { PortfolioSaved } from '@/shared/api';
import type { ToastSeverity } from '@/shared/ui/toastContext';

export interface SavedNotice {
  message: string;
  severity: ToastSeverity;
}

const holdings = (count: number) => `${String(count)} ${count === 1 ? 'holding' : 'holdings'}`;

/**
 * What to tell the owner after the portfolio settings were saved. A first import that did not happen (it failed, was
 * deferred or skipped) is a warning: the settings are saved either way, and the server's message says what to do.
 */
export function savedNotice(saved: PortfolioSaved): SavedNotice {
  const result = saved.import;
  if (!result) return { message: 'Portfolio settings saved.', severity: 'success' };
  switch (result.status) {
    case 'imported':
      return { message: `Imported ${holdings(result.imported)}.`, severity: 'success' };
    case 'failed':
      return {
        message:
          result.message ??
          'Settings saved, but the holdings were not imported. Save again or press Sync now to try again.',
        severity: 'warning',
      };
    case 'deferred':
      return {
        message:
          result.message ?? 'Settings saved. The first sync will import the holdings once the account is connected.',
        severity: 'warning',
      };
    case 'skipped':
      return {
        message: result.message ?? 'Settings saved. The account already has holdings, so nothing was imported.',
        severity: 'warning',
      };
    default:
      return { message: 'Portfolio settings saved.', severity: 'success' };
  }
}
