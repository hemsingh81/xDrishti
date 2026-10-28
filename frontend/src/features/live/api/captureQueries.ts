import { queryOptions } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type LiveCaptureView = Schemas['LiveCaptureView'];
export type CaptureTicket = Schemas['LiveCaptureTicketView'];
export type CaptureFill = Schemas['LiveCaptureFillView'];

export const captureKey = ['live', 'capture'] as const;

/** The capture of one session of a manual-live account: health, tickets, outcomes, fills and the evidence (read only). */
export const captureQuery = (accountId: string, date: string) =>
  queryOptions({
    queryKey: [...captureKey, accountId, date],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/live-trading/accounts/{accountId}/capture', {
          params: { path: { accountId }, query: { date } },
          signal,
        }),
      ),
  });
