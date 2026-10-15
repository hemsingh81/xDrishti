import { parseCommand, type PortfolioNotice } from '@/shared/api';
import {
  attentionKinds,
  severityOf,
  type AppNotification,
  type NotificationAction,
  type NotificationKind,
} from './deriveNotifications';

type PortfolioKind = Extract<
  NotificationKind,
  | 'price_alert_triggered'
  | 'actions_need_decision'
  | 'reconnect_dhan'
  | 'sync_failing'
  | 'holdings_need_review'
  | 'trades_unexplained'
  | 'dividends_pending'
  | 'securities_unmatched'
>;

/**
 * Where Open leads when the notice names no place of its own: a triggered alert to the Portfolio screen with the Alerts panel
 * open on the Triggered tab, items that need a decision to the Portfolio screen, which unfolds the Action center and puts the
 * keyboard on it. The attention items (ADR 0027) carry their own actions; the Portfolio screen is where they lead otherwise.
 */
const hrefs: Readonly<Record<PortfolioKind, string>> = {
  price_alert_triggered: '/portfolio?alerts=triggered',
  actions_need_decision: '/portfolio?focus=actions',
  reconnect_dhan: '/accounts',
  sync_failing: '/portfolio',
  holdings_need_review: '/portfolio',
  trades_unexplained: '/portfolio',
  dividends_pending: '/portfolio',
  securities_unmatched: '/portfolio',
};

const kindOf = (kind: string): PortfolioKind | null =>
  Object.keys(hrefs).includes(kind) ? (kind as PortfolioKind) : null;

/** A path inside this app: starts with one slash (not `//host`, not a backslash trick, not a scheme). */
const isAppPath = (target: string): boolean => /^\/(?![/\\])/.test(target) && !target.includes('\\');

const maxActions = 2;

/**
 * The actions of a notice as the bell shows them: at most two, a route only to a place inside the app and a command only to a
 * "METHOD /api/path" of the API. Anything else is dropped rather than followed.
 */
function actionsOf(notice: PortfolioNotice): NotificationAction[] {
  return (notice.actions ?? [])
    .flatMap((action): NotificationAction[] => {
      if (action.kind === 'route' && isAppPath(action.target)) {
        return [{ label: action.label, kind: 'route', target: action.target }];
      }
      if (action.kind === 'command' && parseCommand(action.target)) {
        return [{ label: action.label, kind: 'command', target: action.target }];
      }
      return [];
    })
    .slice(0, maxActions);
}

/**
 * The Portfolio notices of the API as bell notifications. The id comes from the API (`portfolio:alert:{alertId}`: one per
 * triggered alert; `portfolio:actions:{day}`: one per India day; `attention:…`: the situation is part of it), so read state
 * behaves as it does for the other notices. A kind this version does not know is dropped rather than shown wrongly.
 */
export function derivePortfolioNotifications(notices: readonly PortfolioNotice[]): AppNotification[] {
  return notices.flatMap((notice) => {
    const kind = kindOf(notice.kind);
    if (!kind) return [];
    const actions = actionsOf(notice);
    const at = notice.at ?? notice.since ?? null;
    return [
      {
        id: notice.id,
        kind,
        severity: severityOf(notice.severity),
        accountId: notice.accountId ?? null,
        accountName: null,
        text: notice.text,
        href: actions.find((a) => a.kind === 'route')?.target ?? hrefs[kind],
        at: at === null ? null : Date.parse(at),
        title: notice.title === '' ? null : (notice.title ?? null),
        actions,
        // Only an attention item is identified by `since` (the server re-raises a resolved one with a new `since`). For every
        // other notice the server may stamp `since` anew on each poll, which would make it unread again each time.
        since: attentionKinds.has(kind) ? (notice.since ?? null) : null,
        resolved: notice.resolved,
      },
    ];
  });
}
