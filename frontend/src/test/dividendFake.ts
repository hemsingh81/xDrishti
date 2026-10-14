import { http, HttpResponse } from 'msw';
import type { AcceptSuggestionInput, DividendInput, DividendView, LedgerEntry } from '@/shared/api';
import { sampleHoldings, ids } from './fixtures';
import { buildDividend, buildDividendPage, buildSuggestionPage } from './dividendFixtures';
import { problemResponse } from './server';

export interface DividendFakeOptions {
  /** The dividends entered, newest first. */
  dividends?: readonly DividendView[];
  /** The ledger entries of class dividend offered as suggestions. */
  suggestions?: readonly LedgerEntry[];
}

/** What a request answers while a test breaks it: the status and problem body, once (`null` = it works). */
export interface Problem {
  status: number;
  body: object;
}

/**
 * A stateful fake of the dividends API: adds (with the possible-duplicate rule: the same account, holding, date and amount
 * asks for `confirm`), voids, and the decisions on suggestions change the lists. `calls` says what the app sent; `config`
 * says how the next requests are answered.
 */
export function createDividendFake(options: DividendFakeOptions = {}) {
  const state = { dividends: [...(options.dividends ?? [])], suggestions: [...(options.suggestions ?? [])] };
  const calls = {
    added: [] as DividendInput[],
    voided: [] as string[],
    accepted: [] as { entryId: string; body: AcceptSuggestionInput | null }[],
    dismissed: [] as string[],
  };
  const config = {
    /** One entry per add request, taken in order; an empty list lets adds work. */
    addProblems: [] as Problem[],
    acceptProblem: null as Problem | null,
    dismissProblem: null as Problem | null,
    voidProblem: null as Problem | null,
  };
  const fail = (problem: Problem) => problemResponse(problem.body, problem.status);
  const page = (items: readonly DividendView[], request: Request) => {
    const search = new URL(request.url).searchParams;
    const number = Number(search.get('page') ?? 1);
    const size = Number(search.get('pageSize') ?? 50);
    return HttpResponse.json(
      buildDividendPage(items.slice((number - 1) * size, number * size), {
        page: number,
        pageSize: size,
        total: items.length,
      }),
    );
  };

  const handlers = [
    http.get('/api/holdings/:id/dividends', ({ params, request }) =>
      page(
        state.dividends.filter((d) => d.holdingId === String(params.id)),
        request,
      ),
    ),
    http.get('/api/accounts/:id/dividends/suggestions', ({ request }) => {
      const search = new URL(request.url).searchParams;
      const size = Number(search.get('pageSize') ?? 50);
      return HttpResponse.json(
        buildSuggestionPage(state.suggestions.slice(0, size), { pageSize: size, total: state.suggestions.length }),
      );
    }),
    http.get('/api/accounts/:id/dividends', ({ params, request }) =>
      page(
        state.dividends.filter((d) => d.accountId === String(params.id)),
        request,
      ),
    ),
    http.post('/api/portfolio/dividends', async ({ request }) => {
      const body = (await request.json()) as DividendInput;
      calls.added.push(body);
      const problem = config.addProblems.shift();
      if (problem) return fail(problem);
      const holding = sampleHoldings.find((h) => h.id === body.holdingId);
      const accountId = holding?.accountId ?? body.accountId ?? ids.main;
      const twin = state.dividends.some(
        (d) =>
          !d.voided &&
          d.accountId === accountId &&
          d.holdingId === (body.holdingId ?? null) &&
          d.payDate === body.payDate &&
          d.amount === body.amount,
      );
      if (twin && body.confirm !== true) {
        return problemResponse(
          {
            title: 'A dividend with the same date and amount is already entered.',
            code: 'portfolio.possible_duplicate',
          },
          409,
        );
      }
      const dividend = buildDividend({
        id: `00000000-0000-4000-8000-0000000013${String(calls.added.length).padStart(2, '0')}`,
        accountId,
        holdingId: holding?.id ?? null,
        symbol: holding?.symbol ?? null,
        isin: holding?.isin ?? null,
        payDate: body.payDate ?? '2026-10-01',
        amount: body.amount ?? 1,
        perShare: body.perShare ?? null,
        note: body.note ?? null,
      });
      state.dividends.unshift(dividend);
      return HttpResponse.json(dividend);
    }),
    http.post('/api/portfolio/dividends/:id/void', ({ params }) => {
      if (config.voidProblem) return fail(config.voidProblem);
      const id = String(params.id);
      calls.voided.push(id);
      const found = state.dividends.find((d) => d.id === id);
      if (!found) return problemResponse({ title: 'The dividend does not exist.', code: 'portfolio.not_found' }, 404);
      const voided = { ...found, voided: true, voidedAt: '2026-10-13T08:00:00Z', voidedBy: 'owner' };
      state.dividends = state.dividends.map((d) => (d.id === id ? voided : d));
      return HttpResponse.json(voided);
    }),
    http.post('/api/accounts/:id/dividends/suggestions/:entryId/accept', async ({ params, request }) => {
      const entryId = String(params.entryId);
      const body = (await request.json().catch(() => null)) as AcceptSuggestionInput | null;
      calls.accepted.push({ entryId, body });
      if (config.acceptProblem) return fail(config.acceptProblem);
      const entry = state.suggestions.find((e) => e.id === entryId);
      if (!entry)
        return problemResponse({ title: 'Not a dividend entry of the account.', code: 'portfolio.not_found' }, 404);
      const holding = sampleHoldings.find((h) => h.id === body?.holdingId);
      const dividend = buildDividend({
        id: `00000000-0000-4000-8000-0000000014${String(calls.accepted.length).padStart(2, '0')}`,
        accountId: String(params.id),
        holdingId: holding?.id ?? null,
        symbol: holding?.symbol ?? null,
        payDate: entry.date,
        amount: entry.amount,
        perShare: null,
        note: body?.note ?? null,
      });
      state.dividends.unshift(dividend);
      state.suggestions = state.suggestions.filter((e) => e.id !== entryId);
      return HttpResponse.json(dividend);
    }),
    http.post('/api/accounts/:id/dividends/suggestions/:entryId/dismiss', ({ params }) => {
      const entryId = String(params.entryId);
      calls.dismissed.push(entryId);
      if (config.dismissProblem) return fail(config.dismissProblem);
      state.suggestions = state.suggestions.filter((e) => e.id !== entryId);
      return HttpResponse.json({ ledgerEntryId: entryId, state: 'dismissed', dividendId: null });
    }),
  ];

  return { handlers, state, calls, config };
}
