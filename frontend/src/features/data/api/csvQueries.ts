import { useMutation } from '@tanstack/react-query';
import { api, unwrap, type CsvFilePage, type CsvFileRow } from '@/shared/api';
import type { InfiniteRowsOptions } from '@/shared/lib/useInfiniteRows';
import type { CsvStateFilter } from '../lib/csvReview';
import { dataKeys } from './dataQueries';

export interface CsvFilesFilter {
  q: string;
  state: CsvStateFilter;
}

const fileId = (file: CsvFileRow) => file.name;

/**
 * The CSV folder's files, pending and changed first, pages of 100 loaded as the list is scrolled; `all` leaves the state
 * filter out. Each page also carries the folder, whether it exists and the counts per state.
 */
export const csvFilesList = ({ q, state }: CsvFilesFilter): InfiniteRowsOptions<CsvFileRow, CsvFilePage> => {
  const query = { q: q.trim() || undefined, state: state === 'all' ? undefined : state };
  return {
    queryKey: [...dataKeys.csvFiles, 'list', query],
    getRowId: fileId,
    fetchPage: async (page, { pageSize, signal }) =>
      unwrap(await api.GET('/api/data/csv/files', { params: { query: { ...query, page, pageSize } }, signal })),
  };
};

/** The dry run over up to 20 files; nothing is written, so nothing is reloaded afterwards. */
export function usePreviewCsvFiles() {
  return useMutation({
    mutationFn: async (files: string[]) => unwrap(await api.POST('/api/data/csv/preview', { body: { files } })),
  });
}
