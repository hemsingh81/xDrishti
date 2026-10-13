import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import type { PortfolioHistory } from '@/shared/api';
import { createActionCenterFake, type ActionCenterFakeOptions } from '@/test/actionCenterFake';
import { buildAccountDetail, buildSwingAccount, buildSyncStatus, noValueReturns, summaryOf } from '@/test/fixtures';
import { buildHistory } from '@/test/historyFixtures';
import { scrollToEnd } from '@/test/intersection';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { PortfolioPage } from './components/PortfolioPage';

/** Renders the Portfolio screen at `/portfolio` + `search` (e.g. "?status=closed") with the real providers. */
export function renderPortfolio(search = '') {
  return renderRoutes([{ path: '/portfolio', element: <PortfolioPage /> }], { initialPath: `/portfolio${search}` });
}

/** Both test accounts with the Portfolio role on (by default only the first has it). */
export const twoPortfolioAccounts = () => {
  const swing = buildSwingAccount({ roles: { ...buildSwingAccount().roles, portfolio: { enabled: true } } });
  return http.get('/api/accounts', () => HttpResponse.json([summaryOf(buildAccountDetail()), summaryOf(swing)]));
};

/** No holdings at all: an empty list and totals of zero. */
export const noHoldingsHandlers = () => [
  http.get('/api/holdings', () => HttpResponse.json({ items: [], page: 1, pageSize: 50, total: 0 })),
  http.get('/api/holdings/summary', () =>
    HttpResponse.json({
      returns: noValueReturns({ invested: 0 }),
      openCount: 0,
      closedCount: 0,
      unmappedCount: 0,
      defaultDateLots: 0,
      valuationDate: '2026-10-11',
      lastSync: null,
      dayChange: null,
      dayChangePct: null,
      dayChangeExcluded: 0,
      pricesAsOf: { oldest: null, newest: null },
      benchmarkXirr: null,
      alpha: null,
      benchmarkState: 'insufficient',
      benchmarkAsOf: null,
      alphaBasis: null,
    }),
  ),
  http.get('/api/accounts/:id/holdings/sync-status', () => HttpResponse.json(buildSyncStatus({ last: null }))),
];

/**
 * Answers the value history with `history` (or what a function makes of the request's parameters) and returns the
 * parameters of every request, so a test can say which range and scope were asked for.
 */
export function captureHistory(
  history: PortfolioHistory | ((params: URLSearchParams) => PortfolioHistory) = buildHistory(),
) {
  const requests: URLSearchParams[] = [];
  server.use(
    http.get('/api/portfolio/history', ({ request }) => {
      const params = new URL(request.url).searchParams;
      requests.push(params);
      return HttpResponse.json(typeof history === 'function' ? history(params) : history);
    }),
  );
  return requests;
}

/** The History card, once the page has shown it. */
export const historyCard = () => screen.findByRole('region', { name: 'History' });

/** Scrolls the page far enough that the History card's chart is loaded, and returns the drawn canvas. */
export async function revealChart() {
  await historyCard();
  scrollToEnd();
  return screen.findByTestId('chart-canvas');
}

/** Serves the Action center, the alerts, the thresholds and the notices from one stateful fake (see `createActionCenterFake`). */
export function serveActionCenter(options: ActionCenterFakeOptions = {}) {
  const fake = createActionCenterFake(options);
  server.use(...fake.handlers);
  return fake;
}

/** The Action center card, once the page has shown it (its code loads on demand) and its list has arrived. */
export async function actionCenterCard() {
  const card = await screen.findByRole('region', { name: 'Action center' });
  await waitFor(() => {
    expect(within(card).queryByText('Loading the Action center')).not.toBeInTheDocument();
  });
  return card;
}

/** The list item of the Action center that is about `name` (a symbol, or the account and sector). */
export const actionItem = (card: HTMLElement, name: RegExp | string) => within(card).getByRole('listitem', { name });
