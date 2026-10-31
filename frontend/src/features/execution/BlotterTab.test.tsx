import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildBlotterRow, buildOverview, buildOverviewAccount } from '@/test/executionFixtures';
import { pinClock } from '@/test/pinClock';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { ExecutionPage } from './components/ExecutionPage';
import { recordWrites } from './testing';

pinClock('2026-10-30T04:00:00Z');
recordWrites();

const open = (query = '') =>
  renderRoutes([{ path: '/execution', element: <ExecutionPage /> }], { initialPath: `/execution?tab=orders${query}` });
const rows = (list: unknown[]) => {
  server.use(http.get('/api/execution/blotter', () => HttpResponse.json(list)));
};

describe('Orders tab (blotter)', () => {
  it('shows an order with state word, cause, ids, fill and slippage in ticks and percent', async () => {
    rows([buildBlotterRow({ by: 'owner', algoId: 'ALGO-9', rejectionCodes: [], brokerCode: 'DH-100' })]);
    open();
    const table = await screen.findByRole('table', { name: 'Orders' });
    const row = within(table).getByRole('row', { name: /TATAPOWER/ });
    expect(within(row).getByText('Filled')).toBeInTheDocument();
    expect(within(row).getByText('Owner typed')).toBeInTheDocument();
    expect(within(row).getByText('LIMIT 412.50')).toBeInTheDocument();
    expect(within(row).getByText(/xd-abcdef123/)).toBeInTheDocument();
    expect(within(row).getByText(/120 at 412.60/)).toBeInTheDocument();
    expect(within(row).getByText('2 ticks')).toBeInTheDocument();
    expect(within(row).getByText('0.02 %')).toBeInTheDocument();
    expect(within(row).getByText(/algoId ALGO-9/)).toBeInTheDocument();
    expect(within(row).getByText(/DH-100/)).toBeInTheDocument();
  });

  it('shows No fill, and a non-value slippage as a word, never a number', async () => {
    rows([
      buildBlotterRow({
        intentId: 'i1',
        state: 'open',
        stateGroup: 'working',
        fillPrice: null,
        filledQuantity: 0,
        slippageTicks: { state: 'empty' },
        slippagePercent: { state: 'empty' },
      }),
      buildBlotterRow({
        intentId: 'i2',
        symbol: 'INFY',
        slippageTicks: { state: 'unavailable', reason: 'No ticket price' },
        slippagePercent: { state: 'unavailable', reason: 'No ticket price' },
      }),
    ]);
    open();
    expect(await screen.findByText('No fill')).toBeInTheDocument();
    const infy = screen.getByRole('row', { name: /INFY/ });
    expect(within(infy).getAllByText('Not available')).toHaveLength(2);
  });

  it('shows rejection codes, the canary label and an unknown state in the error style', async () => {
    rows([
      buildBlotterRow({
        intentId: 'i1',
        state: 'rejected',
        stateGroup: 'stopped',
        fillPrice: null,
        rejectionCodes: ['P3.price_collar'],
        quantity: 1,
        canary: true,
      }),
      buildBlotterRow({ intentId: 'i2', symbol: 'WIPRO', state: 'teleported', stateKnown: false, stateGroup: 'other' }),
    ]);
    open();
    const rejected = await screen.findByRole('row', { name: /TATAPOWER/ });
    expect(within(rejected).getByText('Rejected')).toBeInTheDocument();
    expect(within(rejected).getByText(/P3\.price_collar/)).toBeInTheDocument();
    expect(within(rejected).getByText('canary')).toBeInTheDocument();
    expect(screen.getByText('Unknown state: teleported')).toBeInTheDocument();
  });

  it('asks the server for the chosen date, account and state group', async () => {
    server.use(
      http.get('/api/execution/overview', () =>
        HttpResponse.json(buildOverview({ accounts: [buildOverviewAccount()] })),
      ),
    );
    const seen: string[] = [];
    server.use(
      http.get('/api/execution/blotter', ({ request }) => {
        seen.push(new URL(request.url).search);
        return HttpResponse.json([]);
      }),
    );
    open();
    await screen.findByText('No orders for this view.');
    await userEvent.click(screen.getByRole('combobox', { name: 'State group' }));
    await userEvent.click(await screen.findByRole('option', { name: 'working' }));
    await screen.findByText('No orders for this view.');
    expect(seen.at(-1)).toBe('?stateGroup=working');
    await userEvent.click(screen.getByRole('combobox', { name: 'Account' }));
    await userEvent.click(await screen.findByRole('option', { name: 'Test Dhan Main' }));
    await screen.findByText('No orders for this view.');
    expect(seen.at(-1)).toContain('accountId=11111111-1111-4111-8111-111111111111');
  });

  it('shows the empty sentence and an error with retry', async () => {
    rows([]);
    open();
    expect(await screen.findByText('No orders for this view.')).toBeInTheDocument();
  });

  it('shows the canary banner only while the switch is on', async () => {
    rows([]);
    server.use(http.get('/api/execution/overview', () => HttpResponse.json(buildOverview({ canaryPending: true }))));
    open();
    expect(
      await screen.findByText('Canary pending: orders are capped at 1 share until C1 and C2 are recorded'),
    ).toBeInTheDocument();
  });
});
