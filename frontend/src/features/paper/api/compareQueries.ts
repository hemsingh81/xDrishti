import { queryOptions } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';

export type BacktestVsPaper = Schemas['BacktestVsPaperView'];
export type CompareCell = Schemas['CompareCellView'];
export type CompareFanPoint = Schemas['CompareFanPointView'];
export type CompareVerdict = Schemas['CompareVerdictView'];

export const compareKey = ['paper', 'backtest-vs-paper'] as const;

// A GET like every query of the Paper screen: the comparison is computed on read and nothing is written.
export const paperCompareQuery = queryOptions({
  queryKey: compareKey,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/paper/backtest-vs-paper', { signal })),
});
