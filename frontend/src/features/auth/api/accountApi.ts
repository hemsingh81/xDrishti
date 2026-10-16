import { queryOptions, useMutation, useQueryClient } from '@tanstack/react-query';
import { api, ensureOk, unwrap, type Schemas } from '@/shared/api';
import { useStepUp } from '../hooks/useStepUp';
import { sessionQueryKey } from './sessionQuery';

export type OwnerAccount = Schemas['AccountResponse'];
export type AuditEntry = Schemas['AuditEventResponse'];
export type ProfileInput = Schemas['UpdateProfileRequest'];
export type ChangePasswordInput = Schemas['ChangePasswordRequest'];

// Not under 'auth': a sign-out drops every cached query outside that root, and the owner's details must go with it.
export const accountKey = ['account', 'profile'] as const;
const auditKey = ['account', 'sign-in-activity'] as const;

export const recentActivityCount = 10;

/** The signed-in owner's e-mail, display name and when the password last changed. */
export const accountQuery = queryOptions({
  queryKey: accountKey,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/auth/account', { signal })),
});

/** The latest sign-in events, newest first. */
export const signInActivityQuery = queryOptions({
  queryKey: [...auditKey, recentActivityCount],
  queryFn: async ({ signal }) =>
    unwrap(await api.GET('/api/auth/audit', { params: { query: { take: recentActivityCount } }, signal })),
});

/**
 * POST /api/auth/change-password. Other devices are signed out by the API; this session stays. The passwords are the
 * mutation's `variables`, which the observer keeps while the form is mounted: the caller calls `reset()` as soon as the
 * request is over (and keeps what it needs to show, such as the problem), so they are not retained. `gcTime: 0` drops
 * the mutation from the cache once nobody observes it.
 */
export function useChangePassword() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: ChangePasswordInput) => {
      ensureOk(await api.POST('/api/auth/change-password', { body }));
    },
    gcTime: 0,
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: accountKey }),
        queryClient.invalidateQueries({ queryKey: auditKey }),
      ]);
    },
  });
}

/**
 * PUT /api/auth/profile, with the password dialog when the API asks for a fresh check. The session is read again so the
 * avatar initials and the menu show the new name and e-mail.
 */
export function useUpdateProfile() {
  const queryClient = useQueryClient();
  const { withStepUp } = useStepUp();
  return useMutation<OwnerAccount, Error, ProfileInput>({
    mutationFn: withStepUp(async (body: ProfileInput) => unwrap(await api.PUT('/api/auth/profile', { body }))),
    onSuccess: async (account) => {
      queryClient.setQueryData(accountKey, account);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: sessionQueryKey }),
        queryClient.invalidateQueries({ queryKey: auditKey }),
      ]);
    },
  });
}
