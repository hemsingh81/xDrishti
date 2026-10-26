import { queryOptions, useQuery } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api';

export const capabilitiesKey = ['capabilities', 'list'] as const;

/** The owner-act switches, each with its state, and the latest switches. */
export const capabilitiesQuery = () =>
  queryOptions({
    queryKey: capabilitiesKey,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/capabilities', { signal })),
  });

/** Other features ask this before they draw a write control; unknown or not yet loaded means off. */
export function useCapability(name: string): { enabled: boolean; loaded: boolean; failed: boolean } {
  const { data, isError } = useQuery(capabilitiesQuery());
  return {
    enabled: data?.items.find((item) => item.name === name)?.enabled ?? false,
    loaded: data !== undefined,
    failed: isError && data === undefined,
  };
}
