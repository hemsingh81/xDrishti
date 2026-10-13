import { ApiError, type TradePreviewReport } from '@/shared/api';
import { fieldMessages, problemKind, problemText } from './errors';

const isRecord = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null;

/** Whether a value has the parts of a preview that the screen reads. */
function isReport(value: unknown): value is TradePreviewReport {
  return (
    isRecord(value) &&
    isRecord(value.file) &&
    Array.isArray(value.securities) &&
    isRecord(value.notHeld) &&
    isRecord(value.unmatched) &&
    typeof value.digest === 'string'
  );
}

/** The fresh preview that a 409 `portfolio.preview_stale` carries (in the problem's `report`); null for any other error. */
export function staleReport(error: unknown): TradePreviewReport | null {
  if (problemKind(error) !== 'preview_stale' || !(error instanceof ApiError)) return null;
  const report: unknown = error.problem?.report;
  return isReport(report) ? report : null;
}

/** What the API said about the upload's fields (`file`, `accountId`, `digest`), joined; empty when it named none. */
export function uploadFieldText(error: unknown): { file?: string; accountId?: string; other?: string } {
  const { file, accountId, ...rest } = fieldMessages(error);
  const other = Object.values(rest).join(' ');
  return { file, accountId, other: other || undefined };
}

/** The sentence for a failed preview or import: the field messages of a 400, the unsupported upload (415), else the known cases. */
export function uploadErrorText(error: unknown, fallback: string): string {
  if (error instanceof ApiError && error.status === 415) {
    return 'The server did not accept the upload: it has to be a form with the account and the file.';
  }
  if (problemKind(error) === 'invalid') {
    const { file, accountId, other } = uploadFieldText(error);
    const said = [file, accountId, other].filter((part): part is string => part !== undefined).join(' ');
    if (said) return said;
  }
  return problemText(error, fallback);
}
