import type { AccountSummary, RolesInput } from '@/shared/api';

export type DataKind = 'primary' | 'standby';

/** What the owner has ticked for one account. */
export interface RoleDraft {
  data: boolean;
  dataKind: DataKind;
  trading: boolean;
  portfolio: boolean;
}

export const draftOf = (roles: AccountSummary['roles']): RoleDraft => ({
  data: roles.data.enabled,
  dataKind: roles.data.primary ? 'primary' : 'standby',
  trading: roles.trading.enabled,
  portfolio: roles.portfolio.enabled,
});

export const primaryBlockedMessage =
  'The primary Data account cannot be switched off or made standby. Make another account primary first.';

/** The requests that turn the current roles into the wanted ones. */
export interface RolePlan {
  /** Body for PUT /roles, or null when nothing needs it. */
  body: RolesInput | null;
  /** The account must become the primary Data account (separate endpoint; a second primary through PUT roles is refused). */
  makePrimary: boolean;
  /** Why the wanted state is not allowed (checked before anything is sent). */
  blocked?: string;
}

export const isPlanEmpty = (plan: RolePlan): boolean => plan.body === null && !plan.makePrimary && !plan.blocked;

/**
 * Compares the current roles with the wanted ones. Becoming primary goes through make-primary-data; everything else
 * through PUT /roles. `ignorePrimaryLoss` is for the role matrix, where another account is being made primary in the
 * same save (the swap moves this account to standby).
 */
export function planRoleChange(
  current: RoleDraft,
  wanted: RoleDraft,
  options: { ignorePrimaryLoss?: boolean } = {},
): RolePlan {
  const wasPrimary = current.data && current.dataKind === 'primary';
  const losesPrimary = wasPrimary && (!wanted.data || wanted.dataKind !== 'primary');
  if (losesPrimary && !options.ignorePrimaryLoss) {
    return { body: null, makePrimary: false, blocked: primaryBlockedMessage };
  }

  const body: RolesInput = { data: null, trading: null, portfolio: null };
  let changed = false;
  let makePrimary = false;

  if (wanted.data && wanted.dataKind === 'primary') {
    makePrimary = !wasPrimary;
  } else if (wanted.data && (!current.data || current.dataKind !== 'standby') && !losesPrimary) {
    body.data = { enabled: true, kind: 'standby' };
    changed = true;
  } else if (!wanted.data && current.data && !wasPrimary) {
    body.data = { enabled: false, kind: null };
    changed = true;
  }
  if (wanted.trading !== current.trading) {
    body.trading = { enabled: wanted.trading };
    changed = true;
  }
  if (wanted.portfolio !== current.portfolio) {
    body.portfolio = { enabled: wanted.portfolio };
    changed = true;
  }
  return { body: changed ? body : null, makePrimary };
}

/** True when the ticked boxes differ from the saved ones (the primary/standby kind is compared separately). */
export const rolesDiffer = (a: RoleDraft, b: RoleDraft): boolean =>
  a.data !== b.data || a.trading !== b.trading || a.portfolio !== b.portfolio;
