import { queryOptions } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type ExtensionEvaluation = Schemas['ExtensionEvaluationView'];

/** The families the verdict section shows, in order (ADR 0186). */
export const extensionFamilies = ['filter:results', 'ext:ev-1', 'ext:fu-1', 'ext:llm-1', 'pead'] as const;
export type ExtensionFamily = (typeof extensionFamilies)[number];

/** The stored evaluations of one family, newest first (at most 100). */
export const extensionsQuery = (family: ExtensionFamily) =>
  queryOptions({
    queryKey: ['learning', 'extensions', family] as const,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/research/extensions', { params: { query: { family } }, signal })),
  });
