import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query';
import {
  api,
  queryRoots,
  unwrap,
  type ServiceRun,
  type ServiceScheduleInput,
  type ServiceList,
  type ServiceView,
} from '@/shared/api';
import { servicesKeys } from './servicesQueries';

/** After a change: the list, the timeline, the runs, the bell's notices and (the end-of-day run is a data job) the Data screen. */
function reload(queryClient: QueryClient) {
  return Promise.all([
    queryClient.invalidateQueries({ queryKey: servicesKeys.all }),
    queryClient.invalidateQueries({ queryKey: queryRoots.data }),
  ]);
}

/** Puts a service the server just answered with into the list at once, so the table shows it before the list is read again. */
function showService(queryClient: QueryClient, service: ServiceView) {
  queryClient.setQueryData<ServiceList>(servicesKeys.list, (list) =>
    list ? { ...list, items: list.items.map((item) => (item.id === service.id ? service : item)) } : list,
  );
}

/** Saves the dialog's choices (200, the service). 400 `services.invalid` carries `issues`, 409 `services.revision_changed`. */
export function useSaveSchedule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, input }: { id: string; input: ServiceScheduleInput }) =>
      unwrap(await api.PUT('/api/services/{id}/schedule', { params: { path: { id } }, body: input })),
    onSuccess: (service) => {
      showService(queryClient, service);
    },
    onSettled: () => reload(queryClient),
  });
}

/** Restore default: writes the definition's defaults (200, the service). */
export function useResetSchedule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) =>
      unwrap(await api.POST('/api/services/{id}/schedule/reset', { params: { path: { id } } })),
    onSuccess: (service) => {
      showService(queryClient, service);
    },
    onSettled: () => reload(queryClient),
  });
}

/** Pause (200, the service): no scheduled, catch-up or run-after starts; Run now still works. */
export function usePauseService() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => unwrap(await api.POST('/api/services/{id}/pause', { params: { path: { id } } })),
    onSuccess: (service) => {
      showService(queryClient, service);
    },
    onSettled: () => reload(queryClient),
  });
}

export function useResumeService() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => unwrap(await api.POST('/api/services/{id}/resume', { params: { path: { id } } })),
    onSuccess: (service) => {
      showService(queryClient, service);
    },
    onSettled: () => reload(queryClient),
  });
}

/** Shows a run that was just accepted at once: a read that started before the click must not bring back "not running". */
async function showStartedRun(queryClient: QueryClient, run: ServiceRun) {
  await queryClient.cancelQueries({ queryKey: servicesKeys.list });
  if (run.status !== 'running') return;
  queryClient.setQueryData<ServiceList>(servicesKeys.list, (list) =>
    list
      ? {
          ...list,
          items: list.items.map((item) =>
            item.id === run.serviceId ? { ...item, status: 'running', lastRun: run } : item,
          ),
        }
      : list,
  );
}

/** Run now (202, the run, which goes on in the background). 409 `services.already_running` when it runs. */
export function useRunService() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => unwrap(await api.POST('/api/services/{id}/run', { params: { path: { id } } })),
    onSuccess: (run) => showStartedRun(queryClient, run),
    onSettled: () => reload(queryClient),
  });
}
