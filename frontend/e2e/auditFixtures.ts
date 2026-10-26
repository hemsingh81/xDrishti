import { http, HttpResponse } from 'msw';
import type { Schemas } from '../src/shared/api';
import { server } from '../src/test/server';

type AuditPage = Schemas['AuditPageView'];
const auditPage = (items: AuditPage['items']): AuditPage => ({
  window: { from: '2026-09-13', to: '2026-10-12' },
  items,
  nextCursor: null,
  sources: ['auth', 'account', 'approval', 'plan', 'autonomy', 'learning'].map((source) => ({
    source,
    state: 'value',
    reason: null,
  })),
  limits: ['Only what is recorded is shown.'],
});
export const auditEntry = (n: number): AuditPage['items'][number] => ({
  id: `account:00000000-0000-4000-8000-0000000000${String(n).padStart(2, '0')}`,
  at: '2026-10-12T05:00:00Z',
  source: 'account',
  category: 'portfolio_data',
  kind: 'HoldingsImported',
  outcome: 'ok',
  actorKind: 'owner',
  actor: 'owner@example.com',
  accountId: null,
  accountName: null,
  summary: `Holdings imported: ${n} holdings imported`,
  redacted: false,
  planId: null,
  itemId: null,
});
export const mountAudit = (items: AuditPage['items']) => () => {
  server.use(
    http.get('/api/audit/entries', () => HttpResponse.json(auditPage(items))),
    http.get('/api/audit/summary', () =>
      HttpResponse.json({
        window: { from: '2026-09-13', to: '2026-10-12' },
        total: items.length,
        bySource: [],
        byCategory: [],
        byOutcome: [],
        sources: [],
      }),
    ),
    http.get('/api/audit/kinds', () =>
      HttpResponse.json({
        kinds: [{ source: 'auth', kind: 'LoginFailed', label: 'Login failed', category: 'sign_in', outcome: 'failed' }],
        categories: ['sign_in', 'security'],
        outcomes: ['ok', 'failed', 'refused'],
        actors: ['owner', 'system', 'rule', 'cli', 'other'],
      }),
    ),
  );
};
