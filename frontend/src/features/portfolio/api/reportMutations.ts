import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { api, unwrap, type ImportFileResult } from '@/shared/api';
import { brokerHistoryKeys } from './brokerHistoryQueries';
import { costKeys } from './costQueries';
import { invalidateIncome, invalidatePortfolio } from './portfolioMutations';
import { importReports, previewReports, type ReportUpload } from './reportUpload';
import { tradeKeys } from './tradeQueries';

/** The ledger lists (a stored DP charge is one of the account's ledger entries, marked as imported) and the cost imports. */
function invalidateCosts(queryClient: QueryClient) {
  return Promise.all([
    invalidateIncome(queryClient),
    queryClient.invalidateQueries({ queryKey: brokerHistoryKeys.ledgers }),
    queryClient.invalidateQueries({ queryKey: costKeys.batches }),
  ]);
}

/**
 * Reloads exactly what the results changed. Trades: the trade lists and import history and the income figures (the report's
 * charges count in the net returns); with a rebuild, the holdings, lots and totals too. Costs: the income figures, the ledger
 * lists and the cost imports. A demat statement changes no holding: nothing is reloaded for it.
 */
export function invalidateReportImports(queryClient: QueryClient, results: readonly ImportFileResult[]) {
  const tasks: Promise<unknown>[] = [];
  const trades = results.map((result) => result.trades).filter((trades) => trades !== null);
  if (trades.some((result) => result.batch !== null && result.rebuilt)) tasks.push(invalidatePortfolio(queryClient));
  else if (trades.some((result) => result.batch !== null)) {
    tasks.push(queryClient.invalidateQueries({ queryKey: tradeKeys.all }), invalidateIncome(queryClient));
  }
  if (results.some((result) => result.costs?.batch)) tasks.push(invalidateCosts(queryClient));
  return Promise.all(tasks);
}

/** Reads the files: what each is and what importing it would do. Nothing is stored, so nothing is reloaded. */
export function usePreviewReports() {
  return useMutation({ mutationFn: (upload: ReportUpload) => previewReports(upload) });
}

/** Imports the files; what changed is reloaded (see `invalidateReportImports`). */
export function useImportReports() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (upload: ReportUpload) => importReports(upload),
    onSuccess: (response) => invalidateReportImports(queryClient, response.files),
  });
}

/** Undoes a depository-charges import: its entries are voided (nothing is deleted). Doing it twice changes nothing. */
export function useUndoCostBatch() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) =>
      unwrap(await api.POST('/api/portfolio/cost-batches/{id}/undo', { params: { path: { id } } })),
    // Also after a refusal: an import that is undone already changes what the history shows.
    onSettled: () => invalidateCosts(queryClient),
  });
}
