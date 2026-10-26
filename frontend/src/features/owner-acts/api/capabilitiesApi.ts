import { queryOptions, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api';

export const capabilitiesKey = ['capabilities', 'list'] as const;

/** The owner-act switches, each with its state, and the latest switches. */
export const capabilitiesQuery = () =>
  queryOptions({
    queryKey: capabilitiesKey,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/capabilities', { signal })),
  });

/** Other features ask this before they draw a write control; unknown or not yet loaded means off. */
export function useCapability(name: string): { enabled: boolean; loaded: boolean } {
  const { data } = useQuery(capabilitiesQuery());
  return { enabled: data?.items.find((item) => item.name === name)?.enabled ?? false, loaded: data !== undefined };
}

export interface CapabilitySwitch {
  name: string;
  enabled: boolean;
  reason: string;
  /** Only for switching ON, typed for this act; never stored. Switching OFF sends no password at all. */
  password?: string;
}

/** One switch. The answer holds every capability after it, which replaces the cached list. */
export function useSwitchCapability() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ name, enabled, reason, password }: CapabilitySwitch) =>
      unwrap(
        await api.PUT('/api/capabilities/{name}', {
          params: { path: { name } },
          // The generated type lists `password` as a required key; switching OFF sends none (the API does not read it).
          body: (enabled ? { enabled, reason, password } : { enabled, reason }) as {
            enabled: boolean;
            reason: string;
            password: string | null;
          },
        }),
      ),
    onSuccess: ({ capabilities }) => {
      queryClient.setQueryData(capabilitiesKey, capabilities);
    },
  });
}
