import { api, unwrap } from '@/shared/api';
import { externalNavigation } from '@/shared/lib/externalNavigation';
import { refreshAccount } from './accountMutations';
import { useSecuredMutation } from './useSecuredMutation';

const path = (id: string) => ({ params: { path: { id } } });

/**
 * Starts the sign-in at Dhan (password confirmed) and sends the browser to the broker's page in the same tab;
 * the broker returns to /accounts?account={id}&connected=1 or &connectError={code} (see ConnectReturn).
 */
export function useStartConnect() {
  return useSecuredMutation(
    async (id: string) => unwrap(await api.POST('/api/accounts/{id}/connect', path(id))),
    (_queryClient, start) => {
      externalNavigation.assign(start.authorizeUrl);
      return undefined;
    },
  );
}

/** Deletes the stored access token (local only; Dhan tokens end at their expiry anyway). */
export function useDisconnect() {
  return useSecuredMutation(
    async (id: string) => unwrap(await api.DELETE('/api/accounts/{id}/token', path(id))),
    (queryClient, detail) => refreshAccount(queryClient, detail),
  );
}
