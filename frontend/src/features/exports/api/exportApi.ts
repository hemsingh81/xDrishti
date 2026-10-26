import { useMutation } from '@tanstack/react-query';
import { api, ApiError, unwrap, type Schemas } from '@/shared/api';
import { saveBlob } from '../saveBlob';
import { exportWording } from '../wording';

/** The tables of the Reports screens that can be saved; the server holds the same list. */
export type ExportKind = 'report-groups' | 'report-pivot' | 'report-what-works' | 'report-equity' | 'report-trades';

/** What the screen sends: the very fields of its own GET (the filters, and for a grouped table the lens and metric). */
export type ExportQuery = Partial<Record<keyof Schemas['ReportQueryInput'], string>>;

export interface ExportRequest {
  kind: ExportKind;
  query: ExportQuery;
}

export interface ExportedFile {
  /** The name the server made (never ours); a fallback only when the header is missing. */
  name: string;
}

const nameOf = (header: string | null, kind: string): string =>
  /filename="?([A-Za-z0-9._-]+)"?/.exec(header ?? '')?.[1] ?? `xd-${kind}.csv`;

/** The text to show for a refusal: the server's own sentence (403, 413, 422, 429), else a plain line. */
export function exportProblemText(error: unknown): string {
  return error instanceof ApiError ? error.message : exportWording.failed;
}

/**
 * One export: a POST that answers the file, which is saved at once through a temporary link. The bytes never reach the mutation's data (only
 * the name does), the mutation is dropped as soon as it is unobserved (`gcTime` 0) and no query cache entry is made.
 */
export function useExport() {
  return useMutation({
    gcTime: 0,
    mutationFn: async ({ kind, query }: ExportRequest): Promise<ExportedFile> => {
      const result = await api.POST('/api/exports/{kind}', {
        params: { path: { kind } },
        body: { format: 'csv', query },
        parseAs: 'blob',
      });
      const name = nameOf(result.response.headers.get('content-disposition'), kind);
      saveBlob(unwrap(result), name);
      return { name };
    },
  });
}
