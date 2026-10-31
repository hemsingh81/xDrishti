import { screen, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { buildExecStatus, buildOverview, buildOverviewAccount } from '@/test/executionFixtures';
import { pinClock } from '@/test/pinClock';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { ExecutionPage } from './components/ExecutionPage';
import { recordWrites } from './testing';

pinClock('2026-10-30T04:00:00Z');
recordWrites();

beforeEach(() => {
  server.use(
    http.get('/api/execution/readiness', () => HttpResponse.json({ date: '2026-10-30', accounts: [] })),
    http.get('/api/execution/confirm-tickets', () => HttpResponse.json([])),
    http.get('/api/execution/day-unlocks', () => HttpResponse.json([])),
  );
});

const open = () => renderRoutes([{ path: '/execution', element: <ExecutionPage /> }], { initialPath: '/execution' });
const overviewWith = (account = buildOverviewAccount()) => {
  server.use(
    http.get('/api/execution/overview', () =>
      HttpResponse.json(buildOverview({ executionEnabled: true, accounts: [account] })),
    ),
  );
};

describe('Today tab', () => {
  it('shows the mode with its start and the four meters against the sheet in force', async () => {
    overviewWith();
    open();
    const card = await screen.findByRole('region', { name: 'Test Dhan Main today' });
    expect(within(card).getByText('Mode: assisted')).toBeInTheDocument();
    expect(within(card).getByText(/since/)).toBeInTheDocument();
    expect(within(card).getByText('3 of 6')).toBeInTheDocument();
    expect(within(card).getByText('₹1,48,446.00 of ₹1,50,000.00')).toBeInTheDocument();
    expect(within(card).getByText('₹1,470.00 of ₹1,500.00')).toBeInTheDocument();
    expect(within(card).getByText('1 of 3')).toBeInTheDocument();
    expect(within(card).getAllByRole('progressbar')).toHaveLength(4);
  });

  it('shows the pending sheet beside the one in force, with its effective time', async () => {
    const base = buildOverviewAccount();
    overviewWith({
      ...base,
      caps: {
        ...base.caps,
        pendingLimits: {
          id: 5,
          accountId: base.accountId,
          maxOrdersPerDay: 9,
          maxNotionalPerOrder: 1,
          maxNotionalPerDay: 200000,
          maxLossPerDay: 2000,
          maxOpenPositions: 4,
          allowList: [],
          windowStart: null,
          windowEnd: null,
          products: [],
          exitAllOnLossHalt: false,
          actor: 'owner',
          reason: 'raise',
          changedAt: '2026-10-29T10:00:00Z',
          effectiveFrom: '2026-10-31T03:30:00Z',
          snapshotHash: 'h',
        },
      },
    });
    open();
    expect(await screen.findByText(/Pending sheet \(not in force yet\)/)).toHaveTextContent('orders a day 9');
    expect(screen.getByText('3 of 6')).toBeInTheDocument();
  });

  it('never draws zero for an unavailable meter and says why', async () => {
    const base = buildOverviewAccount();
    overviewWith({
      ...base,
      caps: {
        ...base.caps,
        worstCaseLoss: { state: 'unavailable', reason: 'No price for TATAPOWER' },
        orders: { state: 'value', value: 2, cap: null, reason: 'cap_unset' },
      },
    });
    open();
    const card = await screen.findByRole('region', { name: 'Test Dhan Main today' });
    expect(within(card).getByText('Not available')).toBeInTheDocument();
    expect(within(card).getByText('No price for TATAPOWER')).toBeInTheDocument();
    expect(within(card).getByText(/No cap set/)).toBeInTheDocument();
    expect(within(card).queryByText('₹0.00')).not.toBeInTheDocument();
  });

  it('lists the open halts and alarms of the account', async () => {
    const base = buildOverviewAccount();
    overviewWith({
      ...base,
      halts: [
        {
          id: 3,
          at: '2026-10-30T03:50:00Z',
          scope: 'system',
          accountId: null,
          level: 'entries',
          trigger: 'owner',
          source: 'app',
          actor: 'owner',
          reason: 'Owner stop',
          end: null,
        },
      ],
      alarms: [
        {
          id: 9,
          at: '2026-10-30T03:55:00Z',
          code: 'protection_missing',
          severity: 'critical',
          accountId: base.accountId,
          intentPublicId: null,
          detail: 'No stop for TATAPOWER',
          clearedAt: null,
          clearedBy: null,
        },
      ],
    });
    server.use(http.get('/api/execution/status', () => HttpResponse.json(buildExecStatus({ environment: 'sandbox' }))));
    open();
    expect(await screen.findByText(/Halt 3 \(entries\): Owner stop/)).toBeInTheDocument();
    expect(screen.getByText(/protection_missing: No stop for TATAPOWER/)).toBeInTheDocument();
  });

  it('shows an error for the overview with the problem text', async () => {
    server.use(http.get('/api/execution/overview', () => HttpResponse.json({ title: 'no overview' }, { status: 500 })));
    open();
    expect((await screen.findAllByRole('alert')).length).toBeGreaterThan(0);
  });
});
