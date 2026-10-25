import type { Schemas } from '@/shared/api';
import { severityOf, type AppNotification, type NotificationKind } from './deriveNotifications';

/** What the plan record tells the bell (P8-15): a plan to review, a run that failed closed, tickets lapsed, armed, invalidated, fills not matched. A route, no command. */
type PlanNotice = Schemas['PlanNotice'];

type PlanKind = Extract<
  NotificationKind,
  | 'plan_awaiting_review'
  | 'plan_nightly_failed'
  | 'plan_tickets_lapsed'
  | 'plan_tickets_armed'
  | 'plan_tickets_invalidated'
  | 'plan_fills_unmatched'
>;

const kinds: ReadonlySet<string> = new Set<PlanKind>([
  'plan_awaiting_review',
  'plan_nightly_failed',
  'plan_tickets_lapsed',
  'plan_tickets_armed',
  'plan_tickets_invalidated',
  'plan_fills_unmatched',
]);

/** A path inside this app: starts with one slash (not `//host`, not a backslash trick, not a scheme). */
const isAppPath = (target: string): boolean => /^\/(?![/\\])/.test(target) && !target.includes('\\');

/**
 * The plan notices of the API as bell notifications (P8-15). They are read only: Open is the only way on and it goes to the Plans
 * screen (`/plans?plan=<id>`); no button runs a command. The id comes from the API (one per plan and kind), so read state
 * behaves as for the other notices, and an item the API no longer returns simply leaves the list and the badge. A kind this
 * version does not know, or a route outside the app, is dropped rather than followed.
 */
export function derivePlanNotifications(notices: readonly PlanNotice[]): AppNotification[] {
  return notices.flatMap((notice) =>
    kinds.has(notice.kind) && isAppPath(notice.route)
      ? [
          {
            id: notice.id,
            kind: notice.kind as PlanKind,
            severity: severityOf(notice.severity),
            accountId: null,
            accountName: null,
            text: notice.text,
            href: notice.route,
            at: notice.at ? Date.parse(notice.at) : null,
            title: null,
            actions: [],
            resolved: false,
          },
        ]
      : [],
  );
}
