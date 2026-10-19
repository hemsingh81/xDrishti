import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useState } from 'react';
import { useNavigate } from 'react-router';
import { ApiError, queryRoots, runCommand } from '@/shared/api';
import { useToast } from '@/shared/ui/toastContext';
import type { NotificationAction } from '../lib/deriveNotifications';

/** A holdings sync that would close all or most holdings is held back until the owner confirms it (the sync-now dialog does that). */
const needsConfirmation = new Set(['portfolio.confirmation_required', 'portfolio.confirmation_stale']);

/** The words an API refusal came with, else a plain line (a network failure has none). */
function reasonOf(error: unknown): string {
  if (!(error instanceof ApiError)) return 'The app could not be reached.';
  const said = error.problem?.detail ?? error.problem?.title ?? 'The app refused it.';
  return error.problem?.code && needsConfirmation.has(error.problem.code)
    ? `${said} Open the account's Portfolio tab and press Sync now to check and confirm it.`
    : said;
}

/**
 * Does what a notification's button says. A route navigates (the caller closes whatever it was shown in); a command calls the
 * existing endpoint it names with the session and CSRF token, then the notices and what they are about are read again, and a
 * toast tells the result. `busy` names the command that is running (`id|label`), so its button can wait.
 */
export function useRunNotificationAction() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { notify } = useToast();
  const [busy, setBusy] = useState<string | null>(null);

  const run = useCallback(
    async (action: NotificationAction, key: string) => {
      if (action.kind === 'route') {
        void navigate(action.target);
        return;
      }
      setBusy(key);
      try {
        await runCommand(action.target);
        notify(`${action.label}: started. This notice goes away by itself once it works.`, 'success');
      } catch (error) {
        notify(`${action.label} did not work. ${reasonOf(error)}`, 'error');
      } finally {
        setBusy(null);
        // A run changes the sync status, the holdings and the notices; also after a refusal ("already running").
        void queryClient.invalidateQueries({ queryKey: queryRoots.portfolio });
        void queryClient.invalidateQueries({ queryKey: queryRoots.accounts });
      }
    },
    [navigate, queryClient, notify],
  );

  return { run, busy };
}
