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
  const busyId = [run, pause, resume].find((m) => m.isPending)?.variables ?? null;

  return {
    /** The service an action is in flight for (its buttons wait). */
    busyId,
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
      (pausing ? pause : resume).mutate(service.id, {
        onSuccess: () => {
          notify(
            pausing
              ? `${service.name} paused. It does not run by itself until you resume it.`
              : `${service.name} resumed.`,
          );
        },
        onError: (error) => {
          notify(
            actionErrorText(error, service.name, `${service.name} could not be ${pausing ? 'paused' : 'resumed'}.`),
            'error',
          );
        },
      });
    },
  };
}
