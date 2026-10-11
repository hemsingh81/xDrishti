import { useMemo, useState } from 'react';
import type { ImportPreviewRow, ImportRowInput } from '@/shared/api';
import { validateAcquiredOn } from '@/shared/lib/dates';

/**
 * What the owner has chosen for the first import. A row the owner never touched shows `defaultDate` (the first import
 * date field) and is sent without a date of its own (so the API flags it as "default date"); a row whose date was typed
 * or applied is sent with it, even when that equals the default. Rows already imported cannot be chosen or sent.
 */
export function useImportDraft(rows: readonly ImportPreviewRow[], defaultDate: string) {
  const [dates, setDates] = useState<Readonly<Record<string, string>>>({});
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set());

  const importable = useMemo(() => rows.filter((row) => !row.alreadyImported), [rows]);
  const dateOf = (row: ImportPreviewRow): string => dates[row.key] ?? defaultDate;

  const errors = useMemo(() => {
    const found: Record<string, string> = {};
    for (const row of importable) {
      const own = dates[row.key];
      const message = own === undefined ? undefined : validateAcquiredOn(own);
      if (message) found[row.key] = message;
    }
    return found;
  }, [importable, dates]);

  const selectedCount = importable.filter((row) => selected.has(row.key)).length;

  return {
    importable,
    dateOf,
    errors,
    selected,
    selectedCount,
    allSelected: importable.length > 0 && selectedCount === importable.length,
    setDate: (key: string, date: string) => {
      setDates((previous) => ({ ...previous, [key]: date }));
    },
    toggle: (key: string, on: boolean) => {
      setSelected((previous) => {
        const next = new Set(previous);
        if (on) next.add(key);
        else next.delete(key);
        return next;
      });
    },
    toggleAll: (on: boolean) => {
      setSelected(on ? new Set(importable.map((row) => row.key)) : new Set());
    },
    /** Gives the date to every importable row (`'all'`) or to the ticked ones (`'selected'`). */
    applyDate: (date: string, to: 'all' | 'selected') => {
      const keys = importable.filter((row) => to === 'all' || selected.has(row.key)).map((row) => row.key);
      setDates((previous) => ({ ...previous, ...Object.fromEntries(keys.map((key) => [key, date])) }));
    },
    /** The request rows: untouched rows go without a date. */
    toRows: (): ImportRowInput[] => importable.map((row) => ({ key: row.key, acquiredOn: dates[row.key] ?? null })),
  };
}
