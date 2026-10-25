import { queryOptions } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type PaperAccountList = Schemas['PaperAccountList'];
export type PaperAccount = Schemas['PaperAccountView'];
export type PaperPositionList = Schemas['PaperPositionList'];
export type PaperPosition = Schemas['PaperPositionView'];
export type PaperClosedTrade = Schemas['PaperClosedTradeView'];
export type PaperEquity = Schemas['PaperEquityView'];
export type PaperReconcile = Schemas['PaperReconcileView'];
export type MetricView = Schemas['MetricView'];

export const paperKeys = {
  all: ['paper'] as const,
  accounts: ['paper', 'accounts'] as const,
  positions: (id: string) => ['paper', 'positions', id] as const,
  equity: (id: string) => ['paper', 'equity', id] as const,
  reconcile: (id: string) => ['paper', 'reconcile', id] as const,
};

// Every query here is a GET: the Paper screen is read only (nothing is written, no order is placed or exists).
export const paperAccountsQuery = queryOptions({
  queryKey: paperKeys.accounts,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/paper/accounts', { signal })),
});

export const paperPositionsQuery = (id: string) =>
  queryOptions({
    queryKey: paperKeys.positions(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/paper/accounts/{id}/positions', { params: { path: { id } }, signal })),
  });

export const paperEquityQuery = (id: string) =>
  queryOptions({
    queryKey: paperKeys.equity(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/paper/accounts/{id}/equity', { params: { path: { id } }, signal })),
  });

export const paperReconcileQuery = (id: string) =>
  queryOptions({
    queryKey: paperKeys.reconcile(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/paper/accounts/{id}/reconcile', { params: { path: { id } }, signal })),
  });
