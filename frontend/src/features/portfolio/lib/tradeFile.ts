/** The biggest tradebook the API reads (it refuses more): checked here first so a huge file is never uploaded for nothing. */
export const maxTradeFileBytes = 10 * 1024 * 1024;

const extensions = ['.csv', '.txt'] as const;

/** "512 KB", "3.4 MB": the size of a file in words. */
export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${String(bytes)} B`;
  if (bytes < 1024 * 1024) return `${String(Math.round(bytes / 1024))} KB`;
  return `${String(Number((bytes / (1024 * 1024)).toFixed(1)))} MB`;
}

/** Why a chosen file cannot be previewed (a CSV of at most 10 MB that is not empty); undefined when it can. */
export function validateTradeFile(file: { name: string; size: number }): string | undefined {
  const name = file.name.toLowerCase();
  if (!extensions.some((extension) => name.endsWith(extension))) {
    return `“${file.name}” is not a CSV file. Export the tradebook as CSV (.csv) and choose that.`;
  }
  if (file.size === 0) return `“${file.name}” is empty.`;
  if (file.size > maxTradeFileBytes) {
    return `“${file.name}” is ${formatBytes(file.size)}; the limit is ${formatBytes(maxTradeFileBytes)}.`;
  }
  return undefined;
}
