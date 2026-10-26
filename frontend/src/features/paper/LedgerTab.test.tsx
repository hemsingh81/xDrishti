import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http } from 'msw';
import {
  buildPaperAccount,
  buildPaperPosition,
  buildPaperPositions,
  buildPaperReconcile,
  mountPaper,
  otherPaperAccountId,
  paperAccountId,
  paperGone,
} from '@/test/paperFixtures';
import { pinClock } from '@/test/pinClock';
import { renderRoutes } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { PaperPage } from './components/PaperPage';

pinClock('2026-10-26T09:00:00+05:30');

const open = () => renderRoutes([{ path: '/paper', element: <PaperPage /> }], { initialPath: '/paper' });

describe('Paper ledger tab', () => {
  it('shows the tiles, tables, equity line, reconcile status, basis and limits', async () => {
    mountPaper();
    open();

    expect(await screen.findByText('Result after stored charges')).toBeInTheDocument();
    // The result is never labelled "net"; the API's basis text is shown verbatim.
    expect(screen.queryByText(/\bnet\b/i)).not.toBeInTheDocument();
    expect(screen.getByText(/after the stored sheet; the sheet is not confirmed/)).toBeInTheDocument();
    expect(screen.getAllByText(/no order was placed or exists/).length).toBeGreaterThan(0);
    expect(await screen.findByText('limit: no leverage is modelled')).toBeInTheDocument();
    const table = await screen.findByRole('table', { name: 'Open paper positions' });
    expect(within(table).getByText('RELIANCE')).toBeInTheDocument();
    expect(await screen.findByRole('table', { name: 'Closed paper trades' })).toBeInTheDocument();
    expect(await screen.findByRole('img', { name: /Realised equity from .* to .*3 sessions/ })).toBeInTheDocument();
    expect(await screen.findByText(/Reconciliation: ok/)).toBeInTheDocument();
  });

  it('labels the result gross when no stored sheet charged anything', async () => {
    mountPaper({
      accounts: [
        buildPaperAccount({ realised: { gross: 1500, charges: null, net: 1500, basis: 'gross, no charges' } }),
      ],
    });
    open();

    expect(await screen.findByText('Result (gross)')).toBeInTheDocument();
    expect(screen.queryByText('Result after stored charges')).not.toBeInTheDocument();
    expect(screen.getAllByText('gross, no charges').length).toBeGreaterThan(0);
  });

  it('says a stale mark is unavailable, with the reason, and no number', async () => {
    mountPaper({
      positions: buildPaperPositions({
        open: [
          buildPaperPosition({
            mark: { state: 'unavailable', price: null, asOf: null, reason: 'no bar for the last session' },
            unrealised: paperGone('unavailable', 'the mark is unavailable'),
            riskPctOfEquity: paperGone('unavailable', 'no equity'),
          }),
        ],
      }),
    });
    open();

    const table = await screen.findByRole('table', { name: 'Open paper positions' });
    const row = within(table).getByText('RELIANCE').closest('tr');
    if (!row) throw new Error('no row');
    expect(within(row).getByText('mark unavailable')).toBeInTheDocument();
    expect(within(row).getByText('no bar for the last session')).toBeInTheDocument();
    expect(within(row).getByText('Not available')).toBeInTheDocument();
    expect(within(row).queryByText(/2,475|₹0\b|\b0\.00\b/)).not.toBeInTheDocument();
  });

  it('draws a non-value tile as its word and reason, never a digit', async () => {
    mountPaper({
      accounts: [
        buildPaperAccount({
          equity: paperGone('unavailable', 'no capital for a broker-funds account'),
          openRiskPct: paperGone('insufficient', 'no equity to divide by'),
        }),
      ],
    });
    open();

    expect(await screen.findByText('no capital for a broker-funds account')).toBeInTheDocument();
    expect(screen.getByText(/Insufficient data: no equity to divide by/)).toBeInTheDocument();
    const tile = screen.getByText('Equity', { selector: 'p' }).closest('.MuiCard-root');
    if (!(tile instanceof HTMLElement)) throw new Error('no tile');
    expect(tile.textContent).not.toMatch(/\d/);
  });

  it('says the empty state in the exact words', async () => {
    mountPaper({ positions: buildPaperPositions({ open: [], closed: [] }) });
    open();
    expect(
      await screen.findByText('no paper trade yet: a ticket must be approved, armed and captured'),
    ).toBeInTheDocument();
  });

  it('shows differences when the reconciliation finds some', async () => {
    mountPaper({ reconcile: buildPaperReconcile({ status: 'differences', differences: ['exit before entry: TCS'] }) });
    open();
    expect(await screen.findByText('exit before entry: TCS')).toBeInTheDocument();
  });

  it('shows a loading state, then the error of a failed query', async () => {
    server.use(
      http.get('/api/paper/accounts', () =>
        problemResponse({ title: 'paper.invalid', detail: 'The paper ledger failed.' }, 500),
      ),
    );
    open();
    expect(screen.getByRole('status')).toBeInTheDocument();
    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });

  it('switches the data with the account picker', async () => {
    mountPaper({
      accounts: [buildPaperAccount(), buildPaperAccount({ accountId: otherPaperAccountId, name: 'Paper B' })],
      positions: (id) =>
        id === paperAccountId
          ? buildPaperPositions({ accountId: id })
          : buildPaperPositions({ accountId: id, open: [buildPaperPosition({ symbol: 'INFY' })], closed: [] }),
    });
    open();

    expect(await screen.findByText('RELIANCE')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('combobox', { name: 'Paper account' }));
    await userEvent.click(await screen.findByRole('option', { name: 'Paper B' }));
    expect(await screen.findByText('INFY')).toBeInTheDocument();
    expect(screen.queryByText('RELIANCE')).not.toBeInTheDocument();
  });
});
