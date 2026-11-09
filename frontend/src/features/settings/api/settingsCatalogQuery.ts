import { queryOptions, useQuery } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api';

/** The settings catalog: one row per setting with its tier, default and source. */
export const settingsCatalogQuery = queryOptions({
  queryKey: ['settings', 'catalog'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/settings/catalog', { signal })),
});

export function useSettingsCatalog() {
  return useQuery(settingsCatalogQuery);
}
