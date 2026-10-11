import { http, HttpResponse } from 'msw';
import { buildAccountDetail, buildSwingAccount, buildSyncStatus, noValueReturns, summaryOf } from '@/test/fixtures';
import { renderRoutes } from '@/test/render';
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
    }),
  ),
  http.get('/api/accounts/:id/holdings/sync-status', () => HttpResponse.json(buildSyncStatus({ last: null }))),
];
