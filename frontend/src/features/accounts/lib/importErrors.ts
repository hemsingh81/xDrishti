import type { ImportRowInput } from '@/shared/api';

const rowField = /^firstImport\.rows\[(\d+)\]\.(acquiredOn|key)$/;

/**
 * Splits the API's field messages of a rejected save: the ones that belong to a first-import row (by the row's key,
 * since the API counts rows in the order they were sent) and the rest (settings fields, `firstImport.rows`, …).
 */
export function splitImportErrors(
  fields: Readonly<Record<string, string>>,
  rows: readonly ImportRowInput[],
): { byKey: Record<string, string>; rest: Record<string, string> } {
  const byKey: Record<string, string> = {};
  const rest: Record<string, string> = {};
  for (const [field, message] of Object.entries(fields)) {
    const match = rowField.exec(field);
    const key = match ? rows[Number(match[1])]?.key : undefined;
    if (key) byKey[key] = message;
    else rest[field] = message;
  }
  return { byKey, rest };
}
