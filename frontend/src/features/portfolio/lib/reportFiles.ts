import { formatBytes } from './tradeFile';

/** What the API takes in one request: at most 20 files, 10 MB each and 40 MB together. Checked here first, so nothing is uploaded for nothing. */
export const maxReportFiles = 20;
export const maxReportFileBytes = 10 * 1024 * 1024;
export const maxReportTotalBytes = 40 * 1024 * 1024;

/** The API recognises the format from the content, so the name only rules out what is plainly something else. */
const extensions = new Set(['.csv', '.xls', '.xlsx', '.txt']);

/** The extension with its dot, lower case; empty when the name has none (a leading dot alone is not an extension). */
function extensionOf(name: string): string {
  const dot = name.lastIndexOf('.');
  return dot <= 0 ? '' : name.slice(dot).toLowerCase();
}

/**
 * Why one file cannot be previewed (at most 10 MB, not empty, and not plainly another kind of file by its name: CSV, XLS, XLSX,
 * text, or no extension at all, since the format is recognised from the content); undefined when it can.
 */
export function validateReportFile(file: { name: string; size: number }): string | undefined {
  const extension = extensionOf(file.name);
  if (extension !== '' && !extensions.has(extension)) {
    return `“${file.name}” is not a CSV, XLS or XLSX file.`;
  }
  if (file.size === 0) return `“${file.name}” is empty.`;
  if (file.size > maxReportFileBytes) {
    return `“${file.name}” is ${formatBytes(file.size)}; the limit is ${formatBytes(maxReportFileBytes)} per file.`;
  }
  return undefined;
}

const sameFile = (a: File, b: File): boolean => a.name === b.name && a.size === b.size;

/** The files kept after adding `added` to `current`, and what was refused (one sentence each). A file added twice is kept once. */
export function addReportFiles(
  current: readonly File[],
  added: readonly File[],
): { files: File[]; problems: string[] } {
  const files = [...current];
  const problems: string[] = [];
  let total = files.reduce((sum, file) => sum + file.size, 0);
  for (const file of added) {
    const problem = validateReportFile(file);
    if (problem) {
      problems.push(problem);
    } else if (files.some((kept) => sameFile(kept, file))) {
      continue;
    } else if (files.length >= maxReportFiles) {
      problems.push(`“${file.name}” was not added: at most ${String(maxReportFiles)} files can be imported at a time.`);
    } else if (total + file.size > maxReportTotalBytes) {
      problems.push(
        `“${file.name}” was not added: the files together may be at most ${formatBytes(maxReportTotalBytes)}.`,
      );
    } else {
      files.push(file);
      total += file.size;
    }
  }
  return { files, problems };
}
