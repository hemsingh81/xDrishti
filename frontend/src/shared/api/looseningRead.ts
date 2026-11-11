import { queryOptions } from '@tanstack/react-query';
import { api, unwrap } from './client';

// The automatic dismissal setting of the plan screen's loosening banner (BK-10b). It lives here, not in one feature, because the plan
// banner reads it (to say that lines hide by themselves) and the Settings → Limits tab reads and changes it.

/** `plans.loosening_banner.auto_dismiss` (OFF while no row exists) with its version and history. */
export const looseningSettingsQuery = queryOptions({
  queryKey: ['plans', 'loosening-settings'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/plans/loosening-settings', { signal })),
});
