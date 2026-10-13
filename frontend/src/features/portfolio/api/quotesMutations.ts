import { useMutation, useQueryClient } from '@tanstack/react-query';
import { runServiceNow, serviceNoticesQuery, servicesPortKeys, showStartedRun } from '@/shared/api';
import { quotesServiceId } from '../lib/prices';

/** Refresh prices: run the quote service now (202, the run, which goes on in the background). */
export function useRunQuotes() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => runServiceNow(quotesServiceId),
    onSuccess: (run) => showStartedRun(queryClient, run),
    // Also after a refusal: "already running" changes what the list shows.
    onSettled: () => queryClient.invalidateQueries({ queryKey: servicesPortKeys.list }),
  });
}

/** A run ended: the list shows its outcome, and a failure may have raised the bell's notice. */
export function invalidateQuoteStatus(queryClient: ReturnType<typeof useQueryClient>) {
  return Promise.all([
    queryClient.invalidateQueries({ queryKey: servicesPortKeys.list }),
    queryClient.invalidateQueries({ queryKey: serviceNoticesQuery.queryKey }),
  ]);
}
