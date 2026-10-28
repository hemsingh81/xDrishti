import { queryOptions } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type RampView = Schemas['RampView'];
export type LiveSettingsView = Schemas['LiveSettingsView'];

export const rampKey = ['live', 'ramp'] as const;
export const liveSettingsKey = ['live', 'settings'] as const;

/** The ramp of one account: step, scale, risk and history (read only). */
export const rampQuery = (accountId: string) =>
  queryOptions({
    queryKey: [...rampKey, accountId],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/live-trading/ramp', { params: { query: { accountId } }, signal })),
  });

/** The two live switches and their history (read only); both read OFF when nothing is stored. */
export const liveSettingsQuery = queryOptions({
  queryKey: liveSettingsKey,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/live-trading/settings', { signal })),
});
