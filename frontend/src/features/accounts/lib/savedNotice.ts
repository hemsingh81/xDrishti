import type { PortfolioSaved } from '@/shared/api';
import { formatDate } from '@/shared/lib/format';
import type { ToastSeverity } from '@/shared/ui/toastContext';

export interface SavedNotice {
  message: string;
  severity: ToastSeverity;
}

const holdings = (count: number) => `${String(count)} ${count === 1 ? 'holding' : 'holdings'}`;

/** The note after a changed first import date: how many holdings followed it, and how many could not. Undefined when nothing was re-dated. */
function redatedNotice(saved: PortfolioSaved): SavedNotice | undefined {
  const { redated, redateSkipped } = saved.import;
  if (redated <= 0 && redateSkipped <= 0) {
    return undefined;
  }
  const date = saved.account.portfolio.firstImportDate;
  const parts: string[] = [];
  if (redated > 0) {
    parts.push(`Re-dated ${holdings(redated)}${date ? ` to ${formatDate(date)}` : ''}.`);
  } else {
    parts.push('Portfolio settings saved.');
  }
  if (redateSkipped > 0) {
    const lots = `${String(redateSkipped)} ${redateSkipped === 1 ? 'lot' : 'lots'}`;
    parts.push(
      `${lots} could not be re-dated because ${redateSkipped === 1 ? 'it was' : 'they were'} closed before that date.`,
    );
  }
  return { message: parts.join(' '), severity: redated > 0 ? 'success' : 'warning' };
}

/**
 * What to tell the owner after the portfolio settings were saved. A first import that did not happen (it failed or was deferred) is a warning: the settings are saved either way, and the server's message says what to do.
 */
export function savedNotice(saved: PortfolioSaved): SavedNotice {
  const result = saved.import;
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
    case 'skipped': // the account already has holdings: the normal case after the first import (a changed date re-dates some)
      return redatedNotice(saved) ?? { message: 'Portfolio settings saved.', severity: 'success' };
    default:
      return { message: 'Portfolio settings saved.', severity: 'success' };
  }
}
