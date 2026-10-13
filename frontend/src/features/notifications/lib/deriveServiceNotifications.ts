import type { ServiceNotice } from '@/shared/api';
import type { AppNotification, NotificationSeverity } from './deriveNotifications';

const severityOf = (severity: string): NotificationSeverity =>
  severity === 'error' ? 'error' : severity === 'warn' ? 'warn' : 'info';

/**
 * The service notices of the API as bell notifications. The id comes from the API (`service:failed:{serviceId}:{day}`: one
 * notice per service per India day, hence one unread entry however often the run fails; the next successful run clears it). A kind this version does not
 * know is dropped rather than shown wrongly. Open leads to the Services screen.
 */
export function deriveServiceNotifications(notices: readonly ServiceNotice[]): AppNotification[] {
  return notices.flatMap((notice) =>
    notice.kind === 'service_failed'
      ? [
          {
            id: notice.id,
            kind: 'service_failed' as const,
            severity: severityOf(notice.severity),
            accountId: null,
            accountName: null,
            text: notice.text,
            href: '/services',
            at: notice.at === null ? null : Date.parse(notice.at),
          },
        ]
      : [],
  );
}
