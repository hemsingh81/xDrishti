import { useQueryClient, type QueryClient } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';
import { useSearchParams } from 'react-router';
import { useToast, type ToastApi } from '@/shared/ui/toastContext';
import { accountQuery, accountsKeys } from '../api/accountsQueries';
import { connectedMessage } from '../lib/connectedMessage';
import { connectErrorMessage } from '../lib/connectErrors';

/** Fresh data first, so the toast names the broker user the token belongs to and the list dot is current. */
async function announceConnected(queryClient: QueryClient, accountId: string, notify: ToastApi['notify']) {
  try {
    const [detail] = await Promise.all([
      queryClient.query({ ...accountQuery(accountId), staleTime: 0 }),
      queryClient.invalidateQueries({ queryKey: accountsKeys.list }),
      queryClient.invalidateQueries({ queryKey: accountsKeys.activity(accountId) }),
    ]);
    notify(connectedMessage(detail));
  } catch {
    notify('Connected. The account could not be reloaded — refresh the page to see its status.', 'info');
  }
}

/**
 * Handles the broker's return to /accounts?account={id}&connected=1 or …&connectError={code}: selects the account on
 * its Connection tab, says how it went, and removes the outcome from the URL (replace, so Back does not repeat it).
 * Renders nothing; must sit inside the page's ToastProvider.
 */
export function ConnectReturn() {
  const [params, setParams] = useSearchParams();
  const queryClient = useQueryClient();
  const { notify } = useToast();
  const handled = useRef<string | null>(null);
  const accountId = params.get('account');
  const connected = params.get('connected');
  const errorCode = params.get('connectError');

  useEffect(() => {
    if (connected === null && errorCode === null) {
      return;
    }
    // Once per return, also when effects run twice in development.
    const key = `${accountId ?? ''}|${connected ?? ''}|${errorCode ?? ''}`;
    if (handled.current === key) {
      return;
    }
    handled.current = key;
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous);
        next.delete('connected');
        next.delete('connectError');
        if (accountId) {
          next.set('tab', 'connection');
        }
        return next;
      },
      { replace: true },
    );
    if (errorCode !== null) {
      notify(connectErrorMessage(errorCode), 'error');
    } else if (accountId) {
      void announceConnected(queryClient, accountId, notify);
    } else {
      notify('Connected.');
    }
  }, [accountId, connected, errorCode, notify, queryClient, setParams]);

  return null;
}
