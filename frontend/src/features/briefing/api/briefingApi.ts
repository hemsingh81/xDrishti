import { queryOptions } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type Briefing = Schemas['BriefingView'];
export type BriefingKind = 'daily' | 'weekly';

/** A stored text never changes once stored (insert-only): a day or a week is fetched once and kept. */
export const briefingQuery = (kind: BriefingKind, subject: string) =>
  queryOptions({
    queryKey: ['assistant', 'briefing', kind, subject] as const,
    queryFn: async ({ signal }) =>
      kind === 'daily'
        ? unwrap(await api.GET('/api/assistant/briefings/daily', { params: { query: { date: subject } }, signal }))
        : unwrap(await api.GET('/api/assistant/briefings/weekly', { params: { query: { week: subject } }, signal })),
    staleTime: 60_000,
    retry: false,
  });
