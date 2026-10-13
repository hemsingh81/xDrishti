import { http, HttpResponse } from 'msw';
import type {
  HoldingRow,
  ManualTradeInput,
  Trade,
  TradeBatch,
  TradePreviewReport,
  TradeRebuild,
  TradeUndoResult,
} from '@/shared/api';
import { sampleHoldings } from './fixtures';
import { holdStep, pendingResponse } from './gate';
import { readUpload, type Upload } from './multipart';
import { problemResponse } from './server';
import {
  batchIds,
  buildBatch,
  buildBatchPage,
  buildImportResult,
  buildRebuild,
  buildRebuildResult,
  buildReport,
  buildTrade,
  buildTradePage,
  buildUndoResult,
} from './tradeFixtures';

export interface TradeFakeOptions {
  /** The imports, newest first. */
  batches?: readonly TradeBatch[];
  /** All the trades; a holding's list is the ones of its symbol, newest first. */
  trades?: readonly Trade[];
  /** The answer of a preview. */
  report?: TradePreviewReport;
  /** The answer of the rebuild of a holding's lots (the dry run and the real one). */
  rebuild?: TradeRebuild;
}

/** What a request answers while a test breaks it: the status and problem body, once (`null` = it works). */
export interface Problem {
  status: number;
  body: object;
}

/**
 * A stateful fake of the trades API: previews and imports read the multipart form, imports add a batch, undo voids the
 * batch's trades, voids and adds change the lists, rebuilds are counted. `calls` says what the app sent; `config` says how
 * the next requests are answered.
 */
export function createTradeFake(options: TradeFakeOptions = {}) {
  const state = {
    batches: [...(options.batches ?? [])],
    trades: [...(options.trades ?? [])],
  };
  const calls = {
    previews: [] as Upload[],
    imports: [] as Upload[],
    undone: [] as string[],
    undoPreviews: [] as string[],
    voided: [] as string[],
    added: [] as ManualTradeInput[],
    rebuilds: [] as { holdingId: string; dryRun: boolean }[],
  };
  const config = {
    report: options.report ?? buildReport(),
    rebuild: options.rebuild ?? buildRebuild(),
    previewProblem: null as Problem | null,
    /** One entry per import request, taken in order; an empty list lets imports work. */
    importProblems: [] as Problem[],
    undoProblem: null as Problem | null,
    /** What the dry run of an undo says besides the trades voided (lots replaced, holdings not rebuilt, ...). */
    undoPreview: {} as Partial<TradeUndoResult>,
    undoPreviewProblem: null as Problem | null,
    voidProblem: null as Problem | null,
    addProblem: null as Problem | null,
    applyProblem: null as Problem | null,
    dryRunProblem: null as Problem | null,
    /** Voids of these trades are refused (500). */
    failVoids: [] as string[],
    /** Voids answer one at a time, each when the test calls `releaseStep()`. */
    stepVoids: false,
    /** Voids wait until `releasePending()` (503 then). */
    holdVoid: false,
    /** The imports list wait until `releasePending()`. */
    holdBatches: false,
  };

  const symbolOf = (holdingId: string): HoldingRow | undefined => sampleHoldings.find((h) => h.id === holdingId);
  const fail = (problem: Problem) => problemResponse(problem.body, problem.status);

  const handlers = [
    http.post('/api/portfolio/trades/preview', async ({ request }) => {
      calls.previews.push(await readUpload(request));
      return config.previewProblem ? fail(config.previewProblem) : HttpResponse.json(config.report);
    }),
    http.post('/api/portfolio/trades/import', async ({ request }) => {
      const upload = await readUpload(request);
      calls.imports.push(upload);
      const problem = config.importProblems.shift();
      if (problem) return fail(problem);
      const rebuilt = upload.fields.rebuild !== 'false';
      const batch = buildBatch({
        id: `${batchIds.second.slice(0, -3)}${String(state.batches.length + 100)}`,
        fileName: upload.file?.name ?? 'upload.csv',
        imported: config.report.file.newTrades,
        activeTrades: config.report.file.newTrades,
      });
      state.batches.unshift(batch);
      return HttpResponse.json(
        buildImportResult({
          batch,
          imported: config.report.file.newTrades,
          rebuilt,
          holdingsRebuilt: rebuilt ? 1 : 0,
          lotsReplaced: rebuilt ? 1 : 0,
          report: config.report,
        }),
      );
    }),
    http.get('/api/portfolio/trade-batches', async ({ request }) => {
      if (config.holdBatches) return pendingResponse();
      const params = new URL(request.url).searchParams;
      const page = Number(params.get('page') ?? 1);
      const pageSize = Number(params.get('pageSize') ?? 50);
      const items = state.batches.slice((page - 1) * pageSize, page * pageSize);
      return HttpResponse.json(buildBatchPage(items, { page, pageSize, total: state.batches.length }));
    }),
    http.post('/api/portfolio/trade-batches/:id/undo', ({ params, request }) => {
      const id = String(params.id);
      if (new URL(request.url).searchParams.get('dryRun') === 'true') {
        calls.undoPreviews.push(id);
        if (config.undoPreviewProblem) return fail(config.undoPreviewProblem);
        const found = state.batches.find((b) => b.id === id);
        if (!found) return problemResponse({ title: 'The import does not exist.', code: 'portfolio.not_found' }, 404);
        return HttpResponse.json(
          buildUndoResult({
            batch: found,
            dryRun: true,
            applied: false,
            alreadyUndone: found.status === 'undone',
            voided: found.activeTrades,
            ...config.undoPreview,
          }),
        );
      }
      if (config.undoProblem) return fail(config.undoProblem);
      calls.undone.push(id);
      const batch = state.batches.find((b) => b.id === id);
      if (!batch) return problemResponse({ title: 'The import does not exist.', code: 'portfolio.not_found' }, 404);
      if (batch.status === 'undone')
        return HttpResponse.json(buildUndoResult({ batch, alreadyUndone: true, voided: 0 }));
      const undone = { ...batch, status: 'undone', activeTrades: 0, undoneAt: '2026-10-13T08:00:00Z' };
      state.batches = state.batches.map((b) => (b.id === id ? undone : b));
      state.trades = state.trades.map((t) => (t.batchId === id ? { ...t, voided: true, voidedBy: 'owner' } : t));
      return HttpResponse.json(buildUndoResult({ batch: undone, voided: batch.activeTrades }));
    }),
    http.get('/api/holdings/:id/trades', ({ params, request }) => {
      const symbol = symbolOf(String(params.id))?.symbol;
      const all = state.trades.filter((t) => t.symbol === symbol);
      const search = new URL(request.url).searchParams;
      const page = Number(search.get('page') ?? 1);
      const pageSize = Number(search.get('pageSize') ?? 50);
      return HttpResponse.json(
        buildTradePage(all.slice((page - 1) * pageSize, page * pageSize), { page, pageSize, total: all.length }),
      );
    }),
    http.post('/api/portfolio/trades', async ({ request }) => {
      const body = (await request.json()) as ManualTradeInput;
      calls.added.push(body);
      if (config.addProblem) return fail(config.addProblem);
      const holding = symbolOf(body.holdingId ?? '');
      const trade = buildTrade({
        id: `00000000-0000-4000-8000-0000000079${String(calls.added.length).padStart(2, '0')}`,
        accountId: holding?.accountId ?? body.accountId ?? buildTrade().accountId,
        symbol: holding?.symbol ?? body.symbol ?? 'INFY',
        isin: holding?.isin ?? body.isin,
        side: body.side ?? 'buy',
        tradeDate: body.tradeDate ?? '2026-10-05',
        tradeTime: body.tradeTime,
        qty: body.quantity ?? 1,
        price: body.price ?? 1,
        charges: body.charges ?? 0,
        source: 'manual',
        batchId: null,
      });
      state.trades.unshift(trade);
      return HttpResponse.json(trade);
    }),
    http.post('/api/portfolio/trades/:id/void', async ({ params }) => {
      if (config.holdVoid) return pendingResponse();
      if (config.stepVoids) await holdStep();
      const id = String(params.id);
      if (config.failVoids.includes(id)) return problemResponse({ title: 'Could not void that trade.' }, 500);
      if (config.voidProblem) return fail(config.voidProblem);
      calls.voided.push(id);
      const trade = state.trades.find((t) => t.id === id);
      if (!trade) return problemResponse({ title: 'The trade does not exist.', code: 'portfolio.not_found' }, 404);
      const voided = { ...trade, voided: true, voidedAt: '2026-10-13T08:00:00Z', voidedBy: 'owner' };
      state.trades = state.trades.map((t) => (t.id === id ? voided : t));
      return HttpResponse.json(voided);
    }),
    http.post('/api/holdings/:id/rebuild-lots', ({ params, request }) => {
      const dryRun = new URL(request.url).searchParams.get('dryRun') !== 'false';
      const holdingId = String(params.id);
      calls.rebuilds.push({ holdingId, dryRun });
      const problem = dryRun ? config.dryRunProblem : config.applyProblem;
      if (problem) return fail(problem);
      return HttpResponse.json(
        buildRebuildResult(config.rebuild, { dryRun, applied: !dryRun && config.rebuild.status === 'ok' }),
      );
    }),
  ];

  return { handlers, state, calls, config };
}
