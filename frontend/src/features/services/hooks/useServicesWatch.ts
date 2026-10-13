import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useEffectEvent, useRef } from 'react';
import { queryRoots, serviceNoticesQuery, type ServiceView } from '@/shared/api';
import { useRefetchAt } from '@/shared/lib/useRefetchAt';
import { useToast } from '@/shared/ui/toastContext';
import { servicesKeys, servicesQuery } from '../api/servicesQueries';
import { earliestNextRun, isRunning, outcomeOf } from '../lib/outcome';
import { servicesIdlePollMs, servicesRunningPollMs } from '../lib/polling';

/**
 * The list of services, kept fresh. It is read every couple of seconds while any service runs (also one that was
 * already running when the page opened) and once a minute while the page is visible otherwise, and once just after the
 * earliest next run is due, so an open page notices a run that started by itself. When a service that was running is
 * not any more, a toast says how its run ended and everything that depends on it is read again: its history, the
 * bell's notices, and (a run may have changed the stored data) the data and quality screens.
 */
export function useServicesWatch() {
  const queryClient = useQueryClient();
  const toast = useToast();
  const services = useQuery({
    ...servicesQuery,
    // Background tabs are skipped (refetchIntervalInBackground is off): the page catches up when it is shown again.
    refetchInterval: (query) => (query.state.data?.items.some(isRunning) ? servicesRunningPollMs : servicesIdlePollMs),
  });
  const items = services.data?.items;

  const finished = useEffectEvent((service: ServiceView) => {
    for (const queryKey of [
      servicesKeys.runs,
      serviceNoticesQuery.queryKey,
      queryRoots.data,
      queryRoots.quality,
      queryRoots.instruments,
    ]) {
      void queryClient.invalidateQueries({ queryKey });
    }
    const outcome = outcomeOf(service);
    toast.notify(outcome.text, outcome.tone);
  });

  // A service seen running that is not any more has ended its run.
  const watched = useRef(new Set<string>());
  useEffect(() => {
    for (const service of items ?? []) {
      if (isRunning(service)) watched.current.add(service.id);
      else if (watched.current.delete(service.id)) finished(service);
    }
  }, [items]);

  useRefetchAt(items ? earliestNextRun(items) : null, () => {
    void services.refetch();
  });

  return services;
}
