import { ApiError, type DividendPreviewReport } from '@/shared/api';
import { problemKind } from './errors';
import { uploadErrorText } from './tradeErrors';

const isRecord = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null;

/** Whether a value has the parts of a dividend preview that the screen reads. */
function isReport(value: unknown): value is DividendPreviewReport {
  return (
    isRecord(value) &&
    isRecord(value.file) &&
    Array.isArray(value.items) &&
    isRecord(value.totals) &&
    isRecord(value.beforeFirstLot) &&
    typeof value.digest === 'string'
  );
}

/** The fresh preview that a 409 `portfolio.preview_stale` carries (in the problem's `report`); null for any other error. */
export function staleDividendReport(error: unknown): DividendPreviewReport | null {
  if (problemKind(error) !== 'preview_stale' || !(error instanceof ApiError)) return null;
  const report: unknown = error.problem?.report;
  return isReport(report) ? report : null;
}

/** The sentence for a failed dividend preview, import or undo. */
export function dividendErrorText(error: unknown, fallback: string): string {
  if (problemKind(error) === 'conflict') {
    return 'The dividends changed while this was being done (another import may be running). Nothing was changed; read the file again.';
  }
  return uploadErrorText(error, fallback);
}
