import { http, HttpResponse } from 'msw';
import type { CsvFileCounts, CsvFilePage, CsvFilePreview, CsvFileRow } from '@/shared/api';
import { buildCsvPreview, buildErrorPreview, sampleCsvFiles } from './csvFixtures';
import { problemResponse } from './server';

export interface CsvFakeOptions {
  /** The folder's files (default: a mixed sample). */
  files?: readonly CsvFileRow[];
  /** The folder does not exist (the listing says so). */
  folderMissing?: boolean;
  /** Preview answers by file name; a file without one passes. */
  previews?: Readonly<Record<string, CsvFilePreview>>;
}

const folder = '/data/csv';
const rank = (file: CsvFileRow) => (file.state === 'loaded' ? 1 : 0);

/** The API's order: pending and changed first, then loaded, each by name. */
const byApiOrder = (a: CsvFileRow, b: CsvFileRow) => rank(a) - rank(b) || a.name.localeCompare(b.name);

export const countsOf = (files: readonly CsvFileRow[]): CsvFileCounts => ({
  total: files.length,
  pending: files.filter((f) => f.state === 'pending').length,
  loaded: files.filter((f) => f.state === 'loaded').length,
  changed: files.filter((f) => f.state === 'changed').length,
  unmapped: files.filter((f) => f.mapping !== 'mapped').length,
});

/**
 * A stateful stand-in for the CSV review API: the file listing (search, state, paging), the preview (a file passes
 * unless `previews` says otherwise; a name the folder does not have is that file's error; more than 20 files is a 400)
 * and the file states that a load or an undo changes. `calls` records what was asked; `failures` makes reads fail.
 */
export function createCsvFake(options: CsvFakeOptions = {}) {
  let files: CsvFileRow[] = [...(options.files ?? sampleCsvFiles())];
  const calls = { list: [] as string[], preview: [] as string[][] };
  const failures = { list: false, preview: false };

  const handlers = [
    http.get('/api/data/csv/files', ({ request }) => {
      const url = new URL(request.url);
      calls.list.push(url.search);
      if (failures.list) return problemResponse({ title: 'The CSV folder could not be read.', code: 'data.down' }, 500);
      const q = (url.searchParams.get('q') ?? '').trim().toLowerCase();
      const state = url.searchParams.get('state');
      const page = Number(url.searchParams.get('page') ?? 1);
      const pageSize = Number(url.searchParams.get('pageSize') ?? 50);
      const matching = files.filter(
        (f) => !q || f.name.toLowerCase().includes(q) || (f.symbol ?? '').toLowerCase().includes(q),
      );
      const shown = matching.filter((f) => !state || f.state === state).sort(byApiOrder);
      const body: CsvFilePage = {
        folder,
        folderExists: !options.folderMissing,
        items: options.folderMissing ? [] : shown.slice((page - 1) * pageSize, page * pageSize),
        page,
        pageSize,
        total: options.folderMissing ? 0 : shown.length,
        counts: countsOf(matching),
      };
      return HttpResponse.json(body);
    }),
    http.post('/api/data/csv/preview', async ({ request }) => {
      const body = (await request.json()) as { files: string[] | null };
      const names = body.files ?? [];
      calls.preview.push(names);
      if (failures.preview) return problemResponse({ title: 'The preview is not available.', code: 'data.down' }, 500);
      if (names.length === 0 || names.length > 20) {
        return problemResponse(
          { title: 'Choose 1 to 20 files.', code: 'data.invalid', errors: { files: ['Choose 1 to 20 files.'] } },
          400,
        );
      }
      const previews = names.map((name) => {
        const set = options.previews?.[name];
        if (set) return set;
        const file = files.find((f) => f.name === name);
        return file
          ? buildCsvPreview({
              name,
              state: file.state,
              symbol: file.symbol,
              mapping: file.mapping,
              instrument: file.instrument,
              wouldLoad: file.mapping === 'mapped',
            })
          : buildErrorPreview(name);
      });
      return HttpResponse.json({
        files: previews,
        rowsRead: previews.reduce((sum, p) => sum + p.rows, 0),
        comparedRows: previews.reduce((sum, p) => sum + (p.comparison?.comparedRows ?? 0), 0),
      });
    }),
  ];

  const update = (names: readonly string[], patch: (file: CsvFileRow) => CsvFileRow) => {
    files = files.map((file) => (names.includes(file.name) ? patch(file) : file));
  };

  return {
    handlers,
    calls,
    failures,
    find: (name: string) => files.find((file) => file.name === name),
    has: (name: string) => files.some((file) => file.name === name),
    /** A load finished: these files are loaded now. */
    markLoaded: (names: readonly string[]) => {
      update(names, (file) => ({
        ...file,
        state: 'loaded',
        rows: file.rows ?? 1_000,
        firstBar: file.firstBar ?? '2024-04-01T09:15:00+05:30',
        lastBar: file.lastBar ?? '2026-04-30T15:29:00+05:30',
        loadedAt: '2026-10-12T16:05:00+05:30',
        overlapsWith: file.overlapsWith ?? [],
      }));
    },
    /** An undo finished: the file is pending again. */
    markPending: (name: string) => {
      update([name], (file) => ({
        ...file,
        state: 'pending',
        rows: null,
        firstBar: null,
        lastBar: null,
        loadedAt: null,
        overlapsWith: null,
      }));
    },
    get files() {
      return files;
    },
  };
}

export type CsvFake = ReturnType<typeof createCsvFake>;
