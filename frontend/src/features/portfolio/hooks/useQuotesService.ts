import { useQuery } from '@tanstack/react-query';
import { servicesListQuery } from '@/shared/api';
import { quotesServiceId } from '../lib/prices';

const runningPollMs = 2000;
const idlePollMs = 60_000;

/**
 * The quote service as the services list shows it: its schedule, whether it is paused, its last run. Read every couple of
 * seconds while it runs, once a minute while the market is open (`watch`), otherwise only when asked or re-read.
 */
export function useQuotesService(watch: boolean) {
  const query = useQuery({
    ...servicesListQuery,
    refetchInterval: (q) => {
      if (q.state.data?.items.some((s) => s.id === quotesServiceId && s.status === 'running')) return runningPollMs;
      return watch ? idlePollMs : false;
    },
    select: (list) => list.items.find((s) => s.id === quotesServiceId) ?? null,
  });
  return { service: query.data ?? null, isError: query.isError };
}
