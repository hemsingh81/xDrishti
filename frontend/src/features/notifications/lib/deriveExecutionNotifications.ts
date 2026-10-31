import type { ExecutionNotice } from '@/features/execution';
import {
  severityOf,
  type AppNotification,
  type NotificationAction,
  type NotificationKind,
} from './deriveNotifications';

type ExecKind = Extract<NotificationKind, `exec_${string}`>;

/** Where Open leads when the notice names no place of its own. */
const hrefs: Readonly<Record<ExecKind, string>> = {
  exec_confirm_waiting: '/execution',
  exec_auto_action: '/execution?tab=activity',
  exec_order_rejected: '/execution?tab=orders',
  exec_protection_missing: '/execution?tab=positions',
  exec_halted: '/execution',
  exec_kill_switch: '/execution',
  exec_demoted: '/execution?tab=modes',
  exec_recon_break: '/execution?tab=positions',
  exec_not_ready: '/execution',
  exec_ip_invalid: '/execution',
  exec_cap_near: '/execution',
  exec_attended_pause: '/execution',
  exec_day_summary: '/execution?tab=activity',
};

const kindOf = (kind: string): ExecKind | null => (Object.keys(hrefs).includes(kind) ? (kind as ExecKind) : null);

/** A path inside this app: one slash, not `//host`, not a backslash trick. */
const isAppPath = (target: string): boolean => /^\/(?![/\\])/.test(target) && !target.includes('\\');

/**
 * At most two buttons, and only routes inside the app. An execution item never carries a command: the only writes of the
 * Execution screens are the ones the owner presses there (spec P12-10 criterion 15).
 */
const routesOf = (notice: ExecutionNotice): NotificationAction[] =>
  notice.actions
    .filter((a) => a.kind === 'route' && isAppPath(a.target))
    .slice(0, 2)
    .map((a) => ({ label: a.label, kind: 'route', target: a.target }));

/**
 * The execution notices of the API as bell notifications. The id comes from the API and includes the cause
 * (`exec:ticket:{id}`, `exec:halt:{id}`, `exec:action:{logId}`, `exec:day:{date}:{kind}`), so a changed situation is a new item.
 * A kind this version does not know is dropped rather than shown wrongly.
 */
export function deriveExecutionNotifications(notices: readonly ExecutionNotice[]): AppNotification[] {
  return notices.flatMap((notice) => {
    const kind = kindOf(notice.kind);
    if (!kind) return [];
    const actions = routesOf(notice);
    return [
      {
        id: notice.id,
        kind,
        severity: severityOf(notice.severity),
        accountId: notice.accountId,
        accountName: null,
        text: notice.text,
        href: actions[0]?.target ?? hrefs[kind],
        at: Date.parse(notice.at),
        title: notice.title === '' ? null : notice.title,
        actions,
        since: null,
        resolved: notice.resolved,
      },
    ];
  });
}
