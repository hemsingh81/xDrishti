import { useState } from 'react';
import type { ServiceView } from '@/shared/api';
import { useToast } from '@/shared/ui/toastContext';
import { usePauseService, useResumeService, useRunService } from '../api/servicesMutations';
import { actionErrorText, codeOf } from '../lib/errors';

/** Run now, Pause and Resume of a row, each with the toast that says what happened. */
export function useServiceActions() {
  const { notify } = useToast();
  const run = useRunService();
  const pause = usePauseService();
  const resume = useResumeService();
  // A service whose resume needs the owner's password (LC-01): the row asks first, in a dialog, and sends it in the body.
  const [asking, setAsking] = useState<ServiceView | null>(null);
  const busyId = run.isPending
    ? run.variables
    : pause.isPending
      ? pause.variables
      : resume.isPending
        ? resume.variables.id
        : null;

  const resumed = (service: ServiceView) => {
    notify(`${service.name} resumed.`);
  };

  return {
    /** The service an action is in flight for (its buttons wait). */
    busyId,
    /** The service whose resume is waiting for the password, or null. */
    asking,
    /** The password dialog's pending state and refusal (shown as it came; a wrong password does not end the session). */
    resumeState: { pending: resume.isPending, error: resume.error },
    closeAsking: () => {
      setAsking(null);
      resume.reset();
    },
    resumeWithPassword: (service: ServiceView, password: string) => {
      resume.mutate(
        { id: service.id, password },
        {
          onSuccess: () => {
            setAsking(null);
            resume.reset();
            resumed(service);
          },
        },
      );
    },
    runNow: (service: ServiceView) => {
      run.mutate(service.id, {
        onSuccess: (started) => {
          // a service that another process runs (the live feed) only has the request accepted: its run record comes from that process
          notify(
            started.status === 'queued'
              ? 'Requested — the live feed opens its run within 15 seconds.'
              : `${service.name} started.`,
            'info',
          );
        },
        onError: (error) => {
          notify(
            actionErrorText(error, service.name, `${service.name} could not be started.`),
            codeOf(error) === 'services.already_running' ? 'info' : 'error',
          );
        },
      });
    },
    togglePause: (service: ServiceView) => {
      const pausing = service.enabled;
      if (!pausing && service.resumeNeedsPassword) {
        setAsking(service);
        return;
      }
      if (pausing) {
        pause.mutate(service.id, {
          onSuccess: () => {
            notify(`${service.name} paused. It does not run by itself until you resume it.`);
          },
          onError: (error) => {
            notify(actionErrorText(error, service.name, `${service.name} could not be paused.`), 'error');
          },
        });
        return;
      }
      resume.mutate(
        { id: service.id },
        {
          onSuccess: () => {
            resumed(service);
          },
          onError: (error) => {
            notify(actionErrorText(error, service.name, `${service.name} could not be resumed.`), 'error');
          },
        },
      );
    },
  };
}
