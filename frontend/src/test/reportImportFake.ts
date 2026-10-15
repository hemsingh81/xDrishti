import { http, HttpResponse } from 'msw';
import type { CostBatch, ImportFilePreview, ImportFileResult } from '@/shared/api';
import { readUploads, type Uploads } from './multipart';
import { problemResponse } from './server';
import {
  buildCostBatch,
  buildCostBatchPage,
  buildCostImportResult,
  buildCostUndoResult,
  buildFileResult,
  costBatchIds,
} from './reportImportFixtures';
import { batchIds, buildBatch, buildImportResult } from './tradeFixtures';

export interface ReportImportFakeOptions {
  /** What each file is, found by its name; a file that is not here is an unknown file. */
  previews?: readonly ImportFilePreview[];
  /** The imported depository-charges sheets, newest first. */
  costBatches?: readonly CostBatch[];
}

/** What a request answers while a test breaks it: the status and problem body, once (`null` = it works). */
export interface Problem {
  status: number;
  body: object;
}

/** A decision about a name, as the request carries it. */
export interface ReportChoice {
  name: string;
  holdingId?: string;
  instrumentId?: string;
  isin?: string;
  remember: boolean;
}

/** What the app sent in a request, with the JSON text of the lists read back. */
export interface ReportCall {
  fields: Record<string, string>;
  files: string[];
  choices: ReportChoice[];
  digests: (string | null)[];
  acceptOtherAccount: number[];
  rebuild: string | undefined;
}

const callOf = (uploads: Uploads): ReportCall => ({
  fields: uploads.fields,
  files: uploads.files.map((file) => file.name),
  choices: JSON.parse(uploads.fields.choices ?? '[]') as ReportChoice[],
  digests: JSON.parse(uploads.fields.digests ?? '[]') as (string | null)[],
  acceptOtherAccount: JSON.parse(uploads.fields.acceptOtherAccount ?? '[]') as number[],
  rebuild: uploads.fields.rebuild,
});

const unknownPreview = (fileName: string): ImportFilePreview => ({
  fileName,
  kind: 'unknown',
  supported: false,
  routeTo: null,
  message: null,
  error: { code: 'portfolio.wrong_file_kind', message: 'This file is not one of the reports the app reads.' },
  summary: null,
  warnings: [],
  trades: null,
  bill: null,
  costs: null,
  demat: null,
  digest: null,
});

/**
 * A stateful fake of the report import API: a preview answers per file name (another answer once choices are sent), an import
 * stores per kind (a trade batch, a cost batch, learned names) unless a result is queued for the file, the cost batches are
 * listed and undone. `calls` says what the app sent; `config` how requests answer.
 */
export function createReportImportFake(options: ReportImportFakeOptions = {}) {
  const state = {
    previews: new Map((options.previews ?? []).map((preview) => [preview.fileName, preview])),
    costBatches: [...(options.costBatches ?? [])],
  };
  const calls = { previews: [] as ReportCall[], imports: [] as ReportCall[], costUndone: [] as string[] };
  const config = {
    /** The answer for a file once the request carries choices (the names resolved). */
    afterChoices: new Map<string, ImportFilePreview>(),
    previewProblem: null as Problem | null,
    /** One entry per import request, taken in order; an empty list lets imports work. */
    importProblems: [] as Problem[],
    /** Results queued per file name, taken in order, instead of the default for the kind. */
    queued: new Map<string, ImportFileResult[]>(),
    /** The id of the trade batch an import makes (so a test can serve the same batch from the trades fake). */
    tradeBatchId: batchIds.second,
    undoProblem: null as Problem | null,
  };
  const fail = (problem: Problem) => problemResponse(problem.body, problem.status);

  const defaultResult = (preview: ImportFilePreview, call: ReportCall): ImportFileResult => {
    const base = { fileName: preview.fileName, kind: preview.kind };
    const saved = call.choices.filter((choice) => choice.remember).length;
    if (!preview.supported) return buildFileResult({ ...base, status: 'not_supported', message: preview.message });
    if (preview.trades) {
      const added = preview.trades.file.newTrades;
      const rebuilt = call.rebuild !== 'false';
      const batch = buildBatch({
        id: config.tradeBatchId,
        fileName: preview.fileName,
        imported: added,
        activeTrades: added,
      });
      return buildFileResult({
        ...base,
        status: added > 0 ? 'imported' : 'nothing_new',
        trades: buildImportResult({
          batch: added > 0 ? batch : null,
          imported: added,
          rebuilt,
          holdingsRebuilt: rebuilt ? 1 : 0,
          lotsReplaced: rebuilt ? 1 : 0,
          report: preview.trades,
        }),
        aliasesSaved: saved,
      });
    }
    if (preview.costs) {
      const stored = preview.costs.counts.toStore;
      const batch = buildCostBatch({
        id: `${costBatchIds.third.slice(0, -2)}${String(state.costBatches.length + 10)}`,
        fileName: preview.fileName,
        imported: stored,
        activeEntries: stored,
        amount: preview.costs.amountToStore,
      });
      if (stored > 0) state.costBatches.unshift(batch);
      return buildFileResult({
        ...base,
        status: stored > 0 ? 'imported' : 'nothing_new',
        costs: buildCostImportResult({
          batch: stored > 0 ? batch : null,
          imported: stored,
          amount: preview.costs.amountToStore,
          namesLearned: preview.costs.names.new,
        }),
      });
    }
    const names = preview.demat?.names ?? { known: 0, new: 0 };
    return buildFileResult({
      ...base,
      status: names.new > 0 ? 'imported' : 'nothing_new',
      demat: { namesLearned: names.new, namesKnown: names.known },
    });
  };

  const handlers = [
    http.post('/api/portfolio/imports/preview', async ({ request }) => {
      const call = callOf(await readUploads(request));
      calls.previews.push(call);
      if (config.previewProblem) return fail(config.previewProblem);
      return HttpResponse.json({
        files: call.files.map((name) => {
          const resolved = call.choices.length > 0 ? config.afterChoices.get(name) : undefined;
          return resolved ?? state.previews.get(name) ?? unknownPreview(name);
        }),
      });
    }),
    http.post('/api/portfolio/imports/import', async ({ request }) => {
      const call = callOf(await readUploads(request));
      calls.imports.push(call);
      const problem = config.importProblems.shift();
      if (problem) return fail(problem);
      return HttpResponse.json({
        files: call.files.map((name) => {
          const queued = config.queued.get(name)?.shift();
          if (queued) return queued;
          const preview = call.choices.length > 0 ? config.afterChoices.get(name) : undefined;
          return defaultResult(preview ?? state.previews.get(name) ?? unknownPreview(name), call);
        }),
      });
    }),
    http.get('/api/portfolio/cost-batches', ({ request }) => {
      const params = new URL(request.url).searchParams;
      const page = Number(params.get('page') ?? 1);
      const pageSize = Number(params.get('pageSize') ?? 50);
      return HttpResponse.json(
        buildCostBatchPage(state.costBatches.slice((page - 1) * pageSize, page * pageSize), {
          page,
          pageSize,
          total: state.costBatches.length,
        }),
      );
    }),
    http.post('/api/portfolio/cost-batches/:id/undo', ({ params }) => {
      if (config.undoProblem) return fail(config.undoProblem);
      const id = String(params.id);
      calls.costUndone.push(id);
      const batch = state.costBatches.find((b) => b.id === id);
      if (!batch) return problemResponse({ title: 'The import does not exist.', code: 'portfolio.not_found' }, 404);
      if (batch.status === 'undone') {
        return HttpResponse.json(buildCostUndoResult({ batch, alreadyUndone: true, voided: 0 }));
      }
      const undone = { ...batch, status: 'undone', activeEntries: 0, undoneAt: '2026-10-13T08:00:00Z' };
      state.costBatches = state.costBatches.map((b) => (b.id === id ? undone : b));
      return HttpResponse.json(buildCostUndoResult({ batch: undone, voided: batch.activeEntries }));
    }),
  ];

  return { handlers, state, calls, config };
}
