import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query';
import {
  api,
  ensureOk,
  queryRoots,
  unwrap,
  type AccountDetail,
  type ConnectionInput,
  type CreateAccountInput,
  type PortfolioInput,
  type RolesInput,
  type TradingInput,
  type UpdateSecretsInput,
} from '@/shared/api';
import { useToast } from '@/shared/ui/toastContext';
import { savedNotice } from '../lib/savedNotice';
import { accountsKeys } from './accountsQueries';
import { useSecuredMutation } from './useSecuredMutation';

/** After a write to one account: refresh the list, that account and its activity; the rest is marked stale only. */
export function refreshAccount(queryClient: QueryClient, detail: AccountDetail, options: { profiles?: boolean } = {}) {
  queryClient.setQueryData(accountsKeys.detail(detail.id), detail);
  const refreshes = [
    queryClient.invalidateQueries({ queryKey: accountsKeys.list }),
    queryClient.invalidateQueries({ queryKey: accountsKeys.activity(detail.id) }),
  ];
  if (options.profiles) {
    refreshes.push(queryClient.invalidateQueries({ queryKey: accountsKeys.riskProfiles }));
  }
  return Promise.all(refreshes);
}

/** A change of who is primary/standby can touch any account: all details and activity logs are stale. */
function refreshEveryAccount(queryClient: QueryClient, detail: AccountDetail) {
  queryClient.setQueryData(accountsKeys.detail(detail.id), detail);
  return Promise.all([
    queryClient.invalidateQueries({ queryKey: accountsKeys.list }),
    queryClient.invalidateQueries({ queryKey: accountsKeys.details }),
    queryClient.invalidateQueries({ queryKey: accountsKeys.activities }),
    queryClient.invalidateQueries({ queryKey: accountsKeys.riskProfiles }),
  ]);
}

const path = (id: string) => ({ params: { path: { id } } });

/** Creating sends the broker secrets: the request body is never kept after the wizard closes. */
export function useCreateAccount() {
  return useSecuredMutation(
    async (body: CreateAccountInput) => unwrap(await api.POST('/api/accounts', { body })),
    (queryClient, detail) => refreshEveryAccount(queryClient, detail),
    { sensitive: true },
  );
}

export function useUpdateConnection() {
  return useSecuredMutation(
    async ({ id, body }: { id: string; body: ConnectionInput }) =>
      unwrap(await api.PUT('/api/accounts/{id}/connection', { ...path(id), body })),
    (queryClient, detail) => refreshAccount(queryClient, detail),
  );
}

/** Replaces the API key and/or secret (write-only; the answer says only "set" and when). */
export function useUpdateSecrets() {
  return useSecuredMutation(
    async ({ id, body }: { id: string; body: UpdateSecretsInput }) =>
      unwrap(await api.PUT('/api/accounts/{id}/secrets', { ...path(id), body })),
    (queryClient, detail) => refreshAccount(queryClient, detail),
    { sensitive: true },
  );
}

export function useUpdateRoles() {
  return useSecuredMutation(
    async ({ id, body }: { id: string; body: RolesInput }) =>
      unwrap(await api.PUT('/api/accounts/{id}/roles', { ...path(id), body })),
    (queryClient, detail) => refreshEveryAccount(queryClient, detail),
  );
}

export function useMakePrimaryData() {
  return useSecuredMutation(
    async (id: string) => unwrap(await api.POST('/api/accounts/{id}/make-primary-data', path(id))),
    (queryClient, detail) => refreshEveryAccount(queryClient, detail),
  );
}

export function useUpdateTrading() {
  return useSecuredMutation(
    async ({ id, body }: { id: string; body: TradingInput }) =>
      unwrap(await api.PUT('/api/accounts/{id}/trading', { ...path(id), body })),
    (queryClient, detail) => refreshAccount(queryClient, detail, { profiles: true }),
  );
}

export function useArchiveAccount() {
  return useSecuredMutation(
    async (id: string) => {
      ensureOk(await api.DELETE('/api/accounts/{id}', path(id)));
      return id;
    },
    async (queryClient, id) => {
      queryClient.removeQueries({ queryKey: accountsKeys.detail(id) });
      queryClient.removeQueries({ queryKey: accountsKeys.activity(id) });
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: accountsKeys.list }),
        queryClient.invalidateQueries({ queryKey: accountsKeys.details }),
        queryClient.invalidateQueries({ queryKey: accountsKeys.riskProfiles }),
      ]);
    },
  );
}

// Portfolio and Data settings do not need a password check, so these are plain mutations.
/**
 * Saves the portfolio settings; the answer holds the account and what the first import did (when one was asked for).
 * The import creates holdings, so the Portfolio screen's holdings, totals and sync status are marked stale too.
 * `onSaved` is called after the reads were marked stale (for a screen that shows something that depends on the settings).
 */
export function useUpdatePortfolio({ onSaved }: { onSaved?: () => void } = {}) {
  const queryClient = useQueryClient();
  const { notify } = useToast();
  return useMutation({
    mutationFn: async ({ id, body }: { id: string; body: PortfolioInput }) =>
      unwrap(await api.PUT('/api/accounts/{id}/portfolio', { ...path(id), body })),
    onSuccess: async (saved) => {
      // Here, not in the form: saving changes the account, which may remount the form before a per-call callback runs.
      const notice = savedNotice(saved);
      notify(notice.message, notice.severity);
      await Promise.all([
        refreshAccount(queryClient, saved.account),
        // The portfolio feature's keys all start with 'portfolio' (holdings, summary, lots, sync-status).
        queryClient.invalidateQueries({ queryKey: queryRoots.portfolio }),
      ]);
      // A hook-level callback: unlike one passed to `mutate`, it still runs when the form that saved has been replaced.
      onSaved?.();
    },
  });
}

export function useUpdateData() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, liveFeedBudget }: { id: string; liveFeedBudget: number }) =>
      unwrap(await api.PUT('/api/accounts/{id}/data', { ...path(id), body: { liveFeedBudget } })),
    onSuccess: (detail) => refreshAccount(queryClient, detail),
  });
}
