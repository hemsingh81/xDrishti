import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { Schemas } from '@/shared/api';
import { stubPhoneWidth } from '@/test/matchMedia';
import { pinClock } from '@/test/pinClock';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { AuditPage } from './components/AuditPage';

// 2026-10-12 12:00 in India.
pinClock('2026-10-12T06:30:00Z');

type Entry = Schemas['AuditEntryView'];
type Source = Schemas['AuditSourceState'];

const entry = (overrides: Partial<Entry> = {}): Entry => ({
  id: 'account:0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a40',
  at: '2026-10-12T05:00:00Z',
  source: 'account',
  category: 'portfolio_data',
  kind: 'HoldingsImported',
  outcome: 'ok',
  actorKind: 'owner',
  actor: 'owner@example.com',
  accountId: null,
  accountName: null,
  summary: 'Holdings imported: 5 holdings imported',
  redacted: false,
  planId: null,
  itemId: null,
  ...overrides,
});

const allSources = (overrides: Record<string, Partial<Source>> = {}): Source[] =>
  ['auth', 'account', 'approval', 'plan', 'autonomy', 'learning'].map((source) => ({
    source,
    state: 'value',
    reason: null,
    ...overrides[source],
  }));

interface Setup {
  pages?: Schemas['AuditPageView'][];
  fail?: boolean;
  requests?: URL[];
}

function mount({ pages = [], fail = false, requests = [] }: Setup = {}, path = '/audit') {
  let served = 0;
  server.use(
    http.get('/api/audit/entries', ({ request }) => {
      const url = new URL(request.url);
      requests.push(url);
      if (fail) return HttpResponse.json({ title: 'down' }, { status: 500 });
      const page = pages[served] ?? pages[pages.length - 1];
      served += 1;
      return HttpResponse.json(page);
    }),
    http.get('/api/audit/summary', () =>
      HttpResponse.json({
        window: { from: '2026-09-13', to: '2026-10-12' },
        total: 120,
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
    http.get('/api/audit/entries/:id', ({ params }) =>
      HttpResponse.json({
        entry: entry({ id: String(params.id), source: 'auth', kind: 'LoginFailed', summary: 'Login failed' }),
        fields: [
          { label: 'IP address', value: '203.0.113.9' },
          { label: 'User agent', value: 'probe-agent/1' },
        ],
      }),
    ),
  );
  return renderRoutes([{ path: '/audit', element: <AuditPage /> }], { initialPath: path });
}

const page = (items: Entry[], extra: Partial<Schemas['AuditPageView']> = {}): Schemas['AuditPageView'] => ({
  window: { from: '2026-09-13', to: '2026-10-12' },
  items,
  nextCursor: null,
  sources: allSources(),
  limits: ['Only what is recorded is shown.'],
  ...extra,
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('the Audit log screen', () => {
  it('lists the entries in India time with the limits and the total', async () => {
    mount({ pages: [page([entry()])] });

    const table = await screen.findByRole('table', { name: 'Audit entries' });
    expect(await within(table).findByText('Holdings imported: 5 holdings imported')).toBeInTheDocument();
    expect(within(table).getByText('12 Oct 2026, 10:30 IST')).toBeInTheDocument();
    expect(within(table).getByText('Recorded')).toBeInTheDocument();
    expect(await screen.findByText(/Showing 1 of 120 in this range/)).toBeInTheDocument();
    expect(screen.getByText('Only what is recorded is shown.')).toBeInTheDocument();
  });

  it('says so when there is nothing in the range', async () => {
    mount({ pages: [page([])] });

    expect(await screen.findByText('No entries in this range.')).toBeInTheDocument();
  });

  it('shows an error with Retry', async () => {
    mount({ fail: true });

    expect(await screen.findByText('The audit could not be read.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });

  it('never presents a page as complete when a record could not be read', async () => {
    mount({
      pages: [page([entry()], { sources: allSources({ plan: { state: 'unavailable', reason: 'read_failed' } }) })],
    });

    expect(await screen.findByText(/1 of 6 records could not be read/)).toBeInTheDocument();
  });

  it('renders server text as text and marks failed outcomes with a word', async () => {
    mount({
      pages: [
        page([
          entry({
            summary: '<img src=x onerror=alert(1)> Login failed',
            outcome: 'failed',
            actorKind: 'other_address',
            actor: null,
          }),
        ]),
      ],
    });

    const table = await screen.findByRole('table', { name: 'Audit entries' });
    expect(await within(table).findByText('<img src=x onerror=alert(1)> Login failed')).toBeInTheDocument();
    expect(table.querySelector('img')).toBeNull();
    expect(within(table).getByText('Failed')).toBeInTheDocument();
    expect(within(table).getByText('Another address')).toBeInTheDocument();
  });

  it('puts a failed entry of the last 24 hours on the first screen', async () => {
    mount({ pages: [page([entry({ outcome: 'failed', kind: 'LoginFailed' })])] });

    expect(await screen.findByText(/1 failed or refused entry in the last 24 hours/)).toBeInTheDocument();
  });

  it('loads more with the cursor', async () => {
    const requests: URL[] = [];
    mount({
      requests,
      pages: [
        page([entry({ id: 'account:a' })], { nextCursor: 'c1' }),
        page([entry({ id: 'account:b', summary: 'Second page row' })]),
      ],
    });

    await userEvent.click(await screen.findByRole('button', { name: 'Load more' }));

    expect(await screen.findByText('Second page row')).toBeInTheDocument();
    expect(requests.at(-1)?.searchParams.get('cursor')).toBe('c1');
    expect(screen.queryByRole('button', { name: 'Load more' })).not.toBeInTheDocument();
  });

  it('keeps the filters in the URL and sends them', async () => {
    const requests: URL[] = [];
    const { router } = mount({ requests, pages: [page([entry()])] });
    await screen.findByRole('table', { name: 'Audit entries' });

    await userEvent.click(screen.getByRole('button', { name: 'Auth' }));
    await userEvent.click(screen.getByRole('button', { name: '7 days' }));

    await waitFor(() => {
      expect(router.state.location.search).toContain('source=auth');
      expect(router.state.location.search).toContain('range=7d');
    });
    await waitFor(() => {
      const last = requests.at(-1);
      expect(last?.searchParams.get('source')).toBe('auth');
      expect(last?.searchParams.get('from')).toBe('2026-10-06');
      expect(last?.searchParams.get('to')).toBe('2026-10-12');
    });
    await router.navigate(-1);
    await waitFor(() => {
      expect(router.state.location.search).not.toContain('range=7d');
    });
  });

  it('opens the detail with the IP address and user agent and has no export control', async () => {
    mount({
      pages: [page([entry({ id: 'auth:0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a40', source: 'auth', kind: 'LoginFailed' })])],
    });

    await userEvent.click(await screen.findByRole('button', { name: /Details of auth:/ }));

    const dialog = await screen.findByRole('dialog', { name: 'Audit entry' });
    expect(await within(dialog).findByText('203.0.113.9')).toBeInTheDocument();
    expect(within(dialog).getByText('probe-agent/1')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /export|download|copy/i })).not.toBeInTheDocument();
  });

  it('shows cards and a filters drawer on a phone', async () => {
    stubPhoneWidth();
    mount({ pages: [page([entry()])] });

    const list = await screen.findByRole('list', { name: 'Audit entries' });
    expect(within(list).getByText('Holdings imported: 5 holdings imported')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Filters' }));
    expect(await screen.findByRole('group', { name: 'Record' })).toBeInTheDocument();
  });
});
