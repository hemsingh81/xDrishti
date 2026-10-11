import { useQuery } from '@tanstack/react-query';
import type { AccountDetail, ImportPreviewRow, ImportRowInput } from '@/shared/api';
import { holdingsPreviewQuery } from '../api/accountsQueries';
import { connectionState, needsClock } from '../lib/connection';
import { useImportDraft } from './useImportDraft';
import { useNow } from './useNow';

const noRows: readonly ImportPreviewRow[] = [];

/**
 * The first import of an existing account: only when it is connected to Dhan, has the Portfolio role and no holdings
 * yet does it load what Dhan holds and keep the owner's dates. `rows` are the request rows (undefined when there is
 * nothing to import); `defaultDate` is the first import date as typed in the form.
 */
export function useFirstImport(account: AccountDetail, defaultDate: string) {
  const now = useNow(needsClock(account.connection), account.connection.expiresAt);
  const connected = connectionState(account.connection, now).status === 'connected';
  const eligible = account.portfolio.enabled && connected && !account.portfolio.hasHoldings;
  const preview = useQuery({ ...holdingsPreviewQuery(account.id), enabled: eligible });
  const draft = useImportDraft(preview.data?.rows ?? noRows, defaultDate);
  const rows: ImportRowInput[] | undefined = eligible && draft.importable.length > 0 ? draft.toRows() : undefined;
  /** Dhan's holdings are still being fetched: saving now would import nothing, so the form waits. */
  const loading = eligible && preview.isPending;
  /** Dhan's holdings could not be fetched: saving still works, and the first sync imports everything. */
  const failed = eligible && preview.isError;
  return { eligible, connected, preview, draft, rows, loading, failed };
}

export type FirstImport = ReturnType<typeof useFirstImport>;
