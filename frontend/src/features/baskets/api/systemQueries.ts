import { queryOptions } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api';

/** Keys of the system-basket reads: ['baskets', 'system', …] (under `basketsKeys.all`, so a fetch or recompute reloads them). */
export const systemKeys = {
  settings: ['baskets', 'system', 'settings'] as const,
  source: (basketId: string) => ['baskets', 'system', 'source', basketId] as const,
  versions: (basketId: string) => ['baskets', 'system', 'versions', basketId] as const,
  version: (basketId: string, versionId: string) => ['baskets', 'system', 'version', basketId, versionId] as const,
};

/** Which owner-act switches are on (fetch, list files, derived): the buttons are disabled from it, the API stays the authority. */
export const systemSettingsQuery = queryOptions({
  queryKey: systemKeys.settings,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/baskets/system/settings', { signal })),
});

/**
 * AC 11: true when the benchmark in force is Nifty 50 and a Nifty 500 index is tracked, so the screen can hint that broad
 * baskets compare better against Nifty 500. A failed read is no hint (the hint is text only, it changes nothing).
 */
export const broadBenchmarkHintQuery = queryOptions({
  queryKey: ['baskets', 'system', 'benchmark-hint'] as const,
  queryFn: async ({ signal }) => {
    try {
      const benchmark = unwrap(await api.GET('/api/portfolio/benchmark', { signal }));
      if (!/^nifty 50$/i.test((benchmark.name ?? '').trim())) return false;
      const found = unwrap(
        await api.GET('/api/instruments', {
          params: { query: { q: 'Nifty 500', tracked: true, page: 1, pageSize: 20 } },
          signal,
        }),
      );
      return found.items.some((i) => i.type === 'index' && /^nifty 500$/i.test(i.name.trim()) && i.tracked.active);
    } catch {
      return false;
    }
  },
});

/** The source of an index / list basket: its state and last fetches (the reason of a rejection is in them). */
export const sourceQuery = (basketId: string) =>
  queryOptions({
    queryKey: systemKeys.source(basketId),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/baskets/{id}/source', { params: { path: { id: basketId } }, signal })),
  });

/** The published versions of a derived basket, newest first (the drawer shows up to 50). */
export const versionsQuery = (basketId: string) =>
  queryOptions({
    queryKey: systemKeys.versions(basketId),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/baskets/{id}/versions', { params: { path: { id: basketId } }, signal })),
  });

/** One version: the members and the next non-members with score and rank (the "why"). */
export const versionQuery = (basketId: string, versionId: string) =>
  queryOptions({
    queryKey: systemKeys.version(basketId, versionId),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/baskets/{id}/versions/{versionId}', {
          params: { path: { id: basketId, versionId } },
          signal,
        }),
      ),
  });
