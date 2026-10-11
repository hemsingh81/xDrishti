import { useMutation, useQueryClient } from '@tanstack/react-query';
import { isStepUpCancelled } from '@/features/auth';
import type { AccountSummary } from '@/shared/api';
import { accountsQuery } from '../api/accountsQueries';
import { allMessages, fieldErrorsOf, problemMessage } from '../lib/fieldErrors';
import { draftOf, isPlanEmpty, planRoleChange, type RoleDraft } from '../lib/roleChanges';
import { useSaveRoles } from './useSaveRoles';

export interface MatrixInput {
  accounts: AccountSummary[];
  drafts: Record<string, RoleDraft>;
  /** The account ticked as primary Data (undefined when none). */
  primaryId: string | undefined;
}

export interface MatrixResult {
  /** Row messages by account id (only failed rows). */
  errors: Record<string, string>;
  /** Number of accounts that were changed. */
  saved: number;
  /** The owner closed the password dialog: the rest was not attempted. */
  cancelled: boolean;
}

const wantedOf = (draft: RoleDraft, id: string, primaryId: string | undefined): RoleDraft => ({
  ...draft,
  dataKind: id === primaryId ? 'primary' : 'standby',
});

function describeFailure(error: unknown): string {
  const fields = allMessages(fieldErrorsOf(error));
  return fields.length > 0 ? fields.join(' ') : (problemMessage(error) ?? 'Could not save.');
}

/**
 * Saves the role matrix: the account that becomes primary first (make-primary-data swaps the previous primary), then
 * every other changed account against the refreshed state. Failures are reported per row; the password dialog
 * appears on the first request and is not asked again within its window.
 */
export function useSaveRoleMatrix() {
  const queryClient = useQueryClient();
  const { mutateAsync: saveRoles } = useSaveRoles();

  return useMutation({
    mutationFn: async ({ accounts, drafts, primaryId }: MatrixInput): Promise<MatrixResult> => {
      const result: MatrixResult = { errors: {}, saved: 0, cancelled: false };
      const currentPrimaryId = accounts.find((a) => a.roles.data.primary)?.id;
      const newPrimary = primaryId !== undefined && primaryId !== currentPrimaryId ? primaryId : undefined;

      async function apply(account: AccountSummary, wanted: RoleDraft, ignorePrimaryLoss: boolean): Promise<boolean> {
        const plan = planRoleChange(draftOf(account.roles), wanted, { ignorePrimaryLoss });
        if (plan.blocked) {
          result.errors[account.id] = plan.blocked;
          return true;
        }
        if (isPlanEmpty(plan)) {
          return true;
        }
        try {
          await saveRoles({ id: account.id, plan });
          result.saved += 1;
        } catch (error) {
          if (isStepUpCancelled(error)) {
            result.cancelled = true;
            return false;
          }
          result.errors[account.id] = describeFailure(error);
        }
        return true;
      }

      let current = accounts;
      if (newPrimary) {
        const target = accounts.find((a) => a.id === newPrimary);
        const draft = drafts[newPrimary];
        if (target && draft && !(await apply(target, wantedOf(draft, newPrimary, primaryId), true))) {
          return result;
        }
        // The swap changed other accounts (the old primary became standby or lost the role): continue from the truth.
        current = await queryClient.query({ ...accountsQuery, staleTime: 0 });
      }
      for (const account of current) {
        const draft = drafts[account.id];
        if (!draft || account.id === newPrimary) {
          continue;
        }
        if (!(await apply(account, wantedOf(draft, account.id, primaryId), false))) {
          break;
        }
      }
      return result;
    },
  });
}
