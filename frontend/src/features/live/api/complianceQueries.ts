import { queryOptions } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type ComplianceView = Schemas['ComplianceView'];
export type ComplianceRecord = Schemas['ComplianceRecordView'];

export const complianceKey = ['live', 'compliance'] as const;

/** The compliance records, newest first, with the newest of each kind and whether the web may write (read only). */
export const complianceQuery = queryOptions({
  queryKey: complianceKey,
  queryFn: async ({ signal }) =>
    unwrap(await api.GET('/api/live-trading/compliance', { params: { query: { limit: 100 } }, signal })),
});
