import type { DataNotice } from '@/shared/api';
import { severityOf, type AppNotification, type NotificationKind } from './deriveNotifications';

const kinds: Readonly<Record<string, NotificationKind | undefined>> = {
  data_run_failed: 'data_run_failed',
  data_gaps_open: 'data_gaps_open',
  data_quality_failed: 'data_quality_failed',
  data_corporate_actions_pending: 'data_corporate_actions_pending',
};

/**
 * Where Open leads: open gaps to the list of them, failed quality checks to the Data quality screen, suggested corporate
 * actions to their card on the Data screen (the suggestions tab), anything else to the Data screen.
 */
const hrefs: Readonly<Partial<Record<NotificationKind, string>>> = {
  data_gaps_open: '/data?gaps=open',
  data_quality_failed: '/quality',
  data_corporate_actions_pending: '/data?section=actions&actions=suggested',
};
const hrefOf = (kind: NotificationKind): string => hrefs[kind] ?? '/data';

/**
 * The data notices of the API as bell notifications. The id comes from the API (a new failed run or a changed number
 * of open gaps is a new id, hence unread again), so read state behaves as it does for the account notices. A kind this
 * version does not know is dropped rather than shown wrongly.
 */
export function deriveDataNotifications(notices: readonly DataNotice[]): AppNotification[] {
  return notices.flatMap((notice) => {
    const kind = kinds[notice.kind];
    if (!kind) return [];
    return [
      {
        id: notice.id,
        kind,
        severity: severityOf(notice.severity),
        accountId: null,
        accountName: null,
        text: notice.text,
        href: hrefOf(kind),
        at: notice.at === null ? null : Date.parse(notice.at),
        title: null,
        actions: [],
        resolved: false,
      },
    ];
  });
}
