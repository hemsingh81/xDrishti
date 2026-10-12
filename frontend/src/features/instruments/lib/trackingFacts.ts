import type { InstrumentDetail } from '@/shared/api';
import { formatDate, formatDateTime } from '@/shared/lib/format';
import type { Fact } from '@/shared/ui';

const dash = '—';

/** One plain sentence on the tracking state of an instrument. */
export function trackingStatusText(tracked: InstrumentDetail['tracked']): string {
  if (tracked.active) return tracked.since ? `Tracked since ${formatDateTime(tracked.since)}` : 'Tracked';
  if (tracked.deactivatedAt) return `Not tracked (untracked ${formatDateTime(tracked.deactivatedAt)}; history kept)`;
  return 'Not tracked';
}

/** Label/value rows of the Tracking section of the detail drawer. */
export function trackingFacts(tracked: InstrumentDetail['tracked']): Fact[] {
  const from = tracked.dataFrom ? formatDate(tracked.dataFrom) : null;
  const to = tracked.dataTo ? formatDate(tracked.dataTo) : null;
  const coverage = from && to ? `${from} to ${to}` : 'History not collected yet';
  return [
    { label: 'Tracking', value: trackingStatusText(tracked) },
    ...(tracked.retiredButTracked
      ? [{ label: 'Warning', value: "No longer in Dhan's list. It stays tracked until you untrack it." }]
      : []),
    { label: 'Data coverage', value: coverage },
    { label: 'Last update', value: tracked.lastUpdate ? formatDateTime(tracked.lastUpdate) : dash },
    ...(tracked.active && tracked.backfillRequestedAt && !to
      ? [{ label: 'History', value: 'Requested. Collection starts with the EOD service (P1-12).' }]
      : []),
  ];
}
