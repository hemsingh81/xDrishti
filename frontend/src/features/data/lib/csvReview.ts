import type { CsvFilePreview, CsvFileRow } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';

/** Files a preview reads in one request (the API's limit). */
export const maxPreviewFiles = 20;
/** Files one "Load selected" may name (the API's limit, the owner's whole folder). */
export const maxLoadFiles = 214;
export type CsvStateFilter = 'all' | 'pending' | 'changed' | 'loaded';

export const csvStateFilters: readonly CsvStateFilter[] = ['pending', 'changed', 'loaded', 'all'];

export const csvStateLabels: Readonly<Record<CsvStateFilter, string>> = {
  pending: 'Pending',
  changed: 'Changed',
  loaded: 'Loaded',
  all: 'All',
};

/** Why a file cannot be selected, in words (the file name tells the symbol, the instrument list the rest). */
export const mappingProblems: Readonly<Record<string, string>> = {
  unknown: 'Unknown symbol',
  ambiguous: 'Ambiguous symbol',
  invalidName: 'Invalid file name',
};

export const mappingHints: Readonly<Record<string, string>> = {
  unknown: 'No instrument in the instrument list has this symbol, so the file cannot be loaded.',
  ambiguous: 'More than one instrument has this symbol, so the file cannot be loaded.',
  invalidName: 'The name is not <SYMBOL>_1m.csv or .csv.gz, so the file cannot be loaded.',
};

/** Only a file that maps to exactly one instrument can be loaded. */
export const isSelectable = (file: CsvFileRow): boolean => file.mapping === 'mapped';

/** A file that was loaded once: Undo applies (a `changed` one differs in size or date now). */
export const isLoadedFile = (file: Pick<CsvFileRow, 'state'>): boolean =>
  file.state === 'loaded' || file.state === 'changed';

export function formatSize(bytes: number): string {
  if (bytes < 1024) return `${formatNumber(bytes)} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
}

/** Loading files that were loaded already needs `forceCsv`; pending files do not. */
export const needsForce = (files: readonly CsvFileRow[]): boolean => files.some((file) => file.state !== 'pending');

const plural = (count: number, one: string, many: string) => `${formatNumber(count)} ${count === 1 ? one : many}`;

/** "3 pending, 1 changed, 2 loaded (loaded again)" — what the files to load are. */
export function selectionBreakdown(files: readonly CsvFileRow[]): string {
  const count = (state: string) => files.filter((file) => file.state === state).length;
  const parts = [
    count('pending') > 0 ? `${formatNumber(count('pending'))} pending` : null,
    count('changed') > 0 ? `${formatNumber(count('changed'))} changed` : null,
    count('loaded') > 0 ? `${formatNumber(count('loaded'))} already loaded` : null,
  ].filter((part): part is string => part !== null);
  return parts.join(', ');
}

/** The sentence that says how many files are chosen and what the preview will look at. */
export function selectionText(count: number): string {
  if (count === 0) return 'No files selected.';
  const base = `${plural(count, 'file', 'files')} selected.`;
  return count > maxPreviewFiles
    ? `${base} Preview reads the first ${String(maxPreviewFiles)} you checked; Load takes all of them.`
    : base;
}

/** Why a preview file cannot be loaded, or null when it can. */
export function previewBlocker(file: CsvFilePreview): string | null {
  if (file.verdict === 'refused') return `${file.name} is refused: ${file.message ?? 'it does not pass the checks.'}`;
  if (file.verdict === 'error') return `${file.name} has an error: ${file.message ?? 'it could not be read.'}`;
  if (!file.wouldLoad) return `${file.name} does not map to one instrument.`;
  return null;
}

/** What stops "Load selected": nothing chosen, a chosen file that cannot be loaded, or one the preview refused. */
export function loadBlockers(selected: readonly CsvFileRow[], previews: readonly CsvFilePreview[]): string[] {
  if (selected.length === 0) return ['Select at least one file.'];
  const problems = selected
    .filter((file) => !isSelectable(file))
    .map((file) => `${file.name}: ${mappingProblems[file.mapping] ?? file.mapping}.`);
  const names = new Set(selected.map((file) => file.name));
  for (const preview of previews) {
    const reason = names.has(preview.name) ? previewBlocker(preview) : null;
    if (reason) problems.push(reason);
  }
  return problems;
}

/** "3 pass, 1 refused, 1 error" for the live region. */
export function previewTally(files: readonly CsvFilePreview[]): string {
  const count = (verdict: string) => files.filter((file) => file.verdict === verdict).length;
  return `${formatNumber(count('pass'))} pass, ${formatNumber(count('refused'))} refused, ${formatNumber(count('error'))} with an error`;
}

export const verdictLabels: Readonly<Record<string, string>> = { pass: 'Passes', refused: 'Refused', error: 'Error' };
