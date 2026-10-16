import { http, HttpResponse } from 'msw';
import type { DividendBatch, DividendChoice, DividendPreviewReport } from '@/shared/api';
import {
  buildDividendBatch,
  buildDividendBatchPage,
  buildDividendImportResult,
  buildDividendUndoResult,
  buildImportReport,
  dividendBatchIds,
} from './dividendImportFixtures';
import { readUpload, type Upload } from './multipart';
import { problemResponse } from './server';

export interface DividendImportFakeOptions {
  report?: DividendPreviewReport;
  batches?: readonly DividendBatch[];
}

/** What a request answers while a test breaks it: the status and problem body, once (`null` = it works). */
export interface Problem {
  status: number;
  body: object;
}

/** The fields of an import the app sent, with the JSON text of the lists read back. */
export interface DividendImportCall {
  upload: Upload;
  choices: DividendChoice[];
  includeDuplicates: number[];
}

/**
 * A stateful fake of the dividend import API: previews and imports read the multipart form, an import adds a batch and
 * counts the ticked duplicates, undo marks the batch undone. `calls` says what the app sent; `config` how requests answer.
 */
export function createDividendImportFake(options: DividendImportFakeOptions = {}) {
  const state = { batches: [...(options.batches ?? [])] };
  const calls = { previews: [] as Upload[], imports: [] as DividendImportCall[], undone: [] as string[] };
  const config = {
    report: options.report ?? buildImportReport(),
    previewProblem: null as Problem | null,
    /** One entry per import request, taken in order; an empty list lets imports work. */
    importProblems: [] as Problem[],
    undoProblem: null as Problem | null,
    batchesProblem: null as Problem | null,
  };
  const fail = (problem: Problem) => problemResponse(problem.body, problem.status);

  const handlers = [
    http.post('/api/portfolio/dividends/preview', async ({ request }) => {
      calls.previews.push(await readUpload(request));
      return config.previewProblem ? fail(config.previewProblem) : HttpResponse.json(config.report);
    }),
    http.post('/api/portfolio/dividends/import', async ({ request }) => {
      const upload = await readUpload(request);
      const call: DividendImportCall = {
        upload,
        choices: JSON.parse(upload.fields.choices ?? '[]') as DividendChoice[],
        includeDuplicates: JSON.parse(upload.fields.includeDuplicates ?? '[]') as number[],
      };
      calls.imports.push(call);
      const problem = config.importProblems.shift();
      if (problem) return fail(problem);
      const ticked = config.report.items.filter(
        (item) => item.status === 'possible_duplicate' && call.includeDuplicates.includes(item.line),
      );
      const imported = config.report.totals.count + ticked.length;
      const batch = buildDividendBatch({
        id: `${dividendBatchIds.second.slice(0, -2)}${String(state.batches.length + 10)}`,
        fileName: upload.file?.name ?? 'upload.csv',
        imported,
        activeDividends: imported,
        amount: config.report.totals.amount + ticked.reduce((sum, item) => sum + item.amount, 0),
      });
      state.batches.unshift(batch);
      return HttpResponse.json(
        buildDividendImportResult({
          batch,
          imported,
          amount: batch.amount,
          aliasesSaved: call.choices.filter((choice) => choice.remember).length,
          possibleDuplicatesSkipped: config.report.file.possibleDuplicates - ticked.length,
        }),
      );
    }),
    http.get('/api/portfolio/dividend-batches', ({ request }) => {
      if (config.batchesProblem) return fail(config.batchesProblem);
      const params = new URL(request.url).searchParams;
      const page = Number(params.get('page') ?? 1);
      const pageSize = Number(params.get('pageSize') ?? 50);
      return HttpResponse.json(
        buildDividendBatchPage(state.batches.slice((page - 1) * pageSize, page * pageSize), {
          page,
          pageSize,
          total: state.batches.length,
        }),
      );
    }),
    http.post('/api/portfolio/dividend-batches/:id/undo', ({ params }) => {
      if (config.undoProblem) return fail(config.undoProblem);
      const id = String(params.id);
      calls.undone.push(id);
      const batch = state.batches.find((b) => b.id === id);
      if (!batch) return problemResponse({ title: 'The import does not exist.', code: 'portfolio.not_found' }, 404);
      if (batch.status === 'undone')
        return HttpResponse.json(buildDividendUndoResult({ batch, alreadyUndone: true, voided: 0 }));
      const undone = { ...batch, status: 'undone', activeDividends: 0, undoneAt: '2026-10-13T08:00:00Z' };
      state.batches = state.batches.map((b) => (b.id === id ? undone : b));
      return HttpResponse.json(buildDividendUndoResult({ batch: undone, voided: batch.activeDividends }));
    }),
  ];

  return { handlers, state, calls, config };
}
