import type { PortfolioNotice } from '@/shared/api';
import type { AppNotification, NotificationKind, NotificationSeverity } from './deriveNotifications';

type PortfolioKind = Extract<NotificationKind, 'price_alert_triggered' | 'actions_need_decision'>;

/**
 * Where Open leads: a triggered alert to the Portfolio screen with the Alerts panel open on the Triggered tab, items that
 * need a decision to the Portfolio screen, which unfolds the Action center and puts the keyboard on it.
 */
const hrefs: Readonly<Record<PortfolioKind, string>> = {
  price_alert_triggered: '/portfolio?alerts=triggered',
  actions_need_decision: '/portfolio?focus=actions',
};

const kindOf = (kind: string): PortfolioKind | null =>
  kind === 'price_alert_triggered' || kind === 'actions_need_decision' ? kind : null;

const severityOf = (severity: string): NotificationSeverity =>
  severity === 'error' ? 'error' : severity === 'warn' ? 'warn' : 'info';

/**
 * The Portfolio notices of the API as bell notifications. The id comes from the API (`portfolio:alert:{alertId}`: one per
 * triggered alert; `portfolio:actions:{day}`: one per India day), so read state behaves as it does for the other notices.
 * A kind this version does not know is dropped rather than shown wrongly.
 */
export function derivePortfolioNotifications(notices: readonly PortfolioNotice[]): AppNotification[] {
  return notices.flatMap((notice) => {
    const kind = kindOf(notice.kind);
    if (!kind) return [];
    return [
      {
        id: notice.id,
        kind,
        severity: severityOf(notice.severity),
        accountId: null,
        accountName: null,
        text: notice.text,
        href: hrefs[kind],
        at: notice.at === null ? null : Date.parse(notice.at),
      },
    ];
  });
}
