import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api';
import { capabilitiesKey } from './capabilitiesRead';

// The switch (a write): it lives with the lazy owner-acts page. The read side that other features use is capabilitiesRead.ts.

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
