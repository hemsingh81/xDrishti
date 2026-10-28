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
  | 'ticket_level_crossed'
  | 'post_trade_prompt'
  | 'compliance_recheck_overdue'
>;

const kinds: ReadonlySet<string> = new Set<PlanKind>([
  'plan_awaiting_review',
  'plan_nightly_failed',
  'plan_tickets_lapsed',
  'plan_tickets_armed',
  'plan_tickets_invalidated',
  'plan_fills_unmatched',
  'ticket_level_crossed',
  'post_trade_prompt',
  'compliance_recheck_overdue',
]);

/** The only places a plan notice may lead: the Plans screen, optionally on one plan, and the Compliance tab of the Live screen (P11-05). Anything else (another path, a scheme, `//host`, a backslash) is dropped. */
const isPlansRoute = (target: string): boolean => /^\/plans(\?plan=\d+)?$|^\/live\?tab=compliance$/.test(target);

/** The time of a notice in ms, or null when it is missing or not a date. */
const timeOf = (at: string | null | undefined): number | null => {
  const ms = at ? Date.parse(at) : Number.NaN;
  return Number.isNaN(ms) ? null : ms;
};

/**
 * The plan notices of the API as bell notifications (P8-15). They are read only: Open is the only way on and it goes to the Plans
 * screen (`/plans?plan=<id>`); no button runs a command. The id comes from the API (one per plan and kind), so read state
 * behaves as for the other notices, and an item the API no longer returns simply leaves the list and the badge. A kind this
 * version does not know, or a route other than the Plans screen, is dropped rather than followed.
 */
export function derivePlanNotifications(notices: readonly PlanNotice[]): AppNotification[] {
  return notices.flatMap((notice) =>
    kinds.has(notice.kind) && isPlansRoute(notice.route)
      ? [
          {
            id: notice.id,
            kind: notice.kind as PlanKind,
            severity: severityOf(notice.severity),
            accountId: null,
            accountName: null,
            text: notice.text,
            href: notice.route,
            at: timeOf(notice.at),
            title: null,
            actions: [],
            since: notice.since ?? null,
            resolved: notice.resolved,
          },
        ]
      : [],
  );
}
