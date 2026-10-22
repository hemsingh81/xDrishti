import { keepPreviousData, queryOptions } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type StoredStrategy = Schemas['StoredStrategy'];
export type StrategyDetail = Schemas['StrategyDetail'];
export type StrategyVersion = Schemas['StrategyVersionView'];
export type StrategyValidation = Schemas['StrategyValidation'];
export type StrategyIssue = Schemas['StrategyIssue'];
export type CreatedStrategy = Schemas['CreatedStrategy'];
export type ClonedStrategy = Schemas['ClonedStrategy'];

/** Largest text the API takes (413 above it); the upload refuses a larger file before sending anything. */
export const maxTextBytes = 256 * 1024;

export const labKeys = {
  strategies: ['lab', 'strategies'] as const,
};

export const strategiesQuery = queryOptions({
  queryKey: labKeys.strategies,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/lab/strategies', { signal })),
});

export const strategyQuery = (id: string) =>
  queryOptions({
    queryKey: ['lab', 'strategy', id] as const,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/lab/strategies/{id}', { params: { path: { id } }, signal })),
  });

export const versionQuery = (id: string, version: number) =>
  queryOptions({
    queryKey: ['lab', 'strategy', id, 'version', version] as const,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/lab/strategies/{id}/versions/{version}', { params: { path: { id, version } }, signal }),
      ),
  });

/** The JSON Schema of the strategy file, as text (the editor parses it). */
export const schemaQuery = queryOptions({
  queryKey: ['lab', 'schema'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/lab/schema', { parseAs: 'text', signal })),
  staleTime: Infinity,
});

/** Validate writes nothing, so it is a query keyed by the text. */
export const validateQuery = (text: string) =>
  queryOptions({
    queryKey: ['lab', 'validate', text] as const,
    queryFn: async ({ signal }) =>
      unwrap(await api.POST('/api/lab/validate', { body: { text, format: 'yaml' }, signal })),
    staleTime: Infinity,
    gcTime: 60_000,
    placeholderData: keepPreviousData,
  });

export const downloadHref = (id: string, version: number) =>
  `/api/lab/strategies/${encodeURIComponent(id)}/versions/${version}/file`;

export async function createStrategy(input: { text: string; parentHash: string | null }) {
  return unwrap(await api.POST('/api/lab/strategies', { body: input }));
}

export async function cloneVersion(id: string, version: number) {
  return unwrap(
    await api.POST('/api/lab/strategies/{id}/versions/{version}/clone', {
      params: { path: { id, version } },
      body: {},
    }),
  );
}
