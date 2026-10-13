import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildLot, buildLots, buildReturns, holdingIds } from '@/test/fixtures';
import { problemResponse, server } from '@/test/server';
import { renderPortfolio, serveTrades, warmTrades } from './testing';

beforeAll(warmTrades, 60_000);

const lot = (n: number) => `00000000-0000-4000-8000-0000000006${String(n).padStart(2, '0')}`;

/** The lots after a rebuild: the opening lot (what the trades do not explain) and two lots from trades. */
const rebuiltLots = () =>
  buildLots({
    lots: [
      buildLot({
        id: lot(1),
        acquiredOn: '2024-06-09',
        qty: 10,
        unitCost: 1_500,
        usesDefaultDate: true,
        source: 'import',
        returns: buildReturns({ invested: 15_000, value: 17_000, pnl: 2_000, pnlPct: 13.3, xirr: 9.1 }),
      }),
      buildLot({ id: lot(2), acquiredOn: '2024-06-10', qty: 10, unitCost: 1_400, source: 'trade' }),
      buildLot({
        id: lot(3),
        acquiredOn: '2025-02-03',
        qty: 5,
        unitCost: 1_650,
        source: 'trade',
        status: 'closed',
        closedOn: '2026-09-20',
        closePrice: 1_700,
      }),
    ],
  });

async function openLots(lots = rebuiltLots()) {
  serveTrades();
  server.use(http.get('/api/holdings/:id/lots', () => HttpResponse.json(lots)));
  renderPortfolio(`?holding=${holdingIds.infy}`);
  const drawer = within(await screen.findByLabelText('Holding details'));
  const table = within(await drawer.findByRole('table', { name: 'Lots' }));
  return { drawer, table };
}

describe('Lots after a rebuild from trades', () => {
  it('labels lots made from trades “From trades” and does not let their date be edited', async () => {
    const { table } = await openLots();

    const row = within(table.getByRole('row', { name: /10 Jun 2024/ }));
    expect(row.getByText('From trades')).toBeInTheDocument();
    expect(row.queryByText('Default date')).not.toBeInTheDocument();
    expect(row.queryByRole('button', { name: /^Edit the date/ })).not.toBeInTheDocument();
    const locked = row.getByRole('button', {
      name: 'The date of the lot bought on 10 Jun 2024 comes from your trades and cannot be edited',
    });
    expect(locked).toHaveAttribute('aria-disabled', 'true');

    await userEvent.click(locked);
    expect(screen.queryByLabelText(/^Acquired on, lot of/)).not.toBeInTheDocument();
  });

  it('says why in a tooltip, from the pointer and from the keyboard', async () => {
    const { table } = await openLots();
    const locked = table.getAllByRole('button', { name: /comes from your trades and cannot be edited/ });
    expect(locked).toHaveLength(2);

    await userEvent.hover(locked[0]!);
    expect(
      await screen.findByRole('tooltip', { name: /This lot comes from your trades, so its date is the trade date/ }),
    ).toBeInTheDocument();
    await userEvent.unhover(locked[0]!);
    await waitFor(() => {
      expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
    });

    await userEvent.tab();
    act(() => {
      locked[1]!.focus();
    });
    expect(await screen.findByRole('tooltip')).toHaveTextContent(/Change the trades and rebuild the lots to change it/);
  });

  it('labels the opening lot “Before the first trade” and keeps its date editable', async () => {
    const { table } = await openLots();

    const row = within(table.getByRole('row', { name: /9 Jun 2024/ }));
    expect(row.getByText('Before the first trade')).toBeInTheDocument();
    expect(row.getByText('Default date')).toBeInTheDocument();
    expect(row.getByRole('button', { name: 'Edit the date of the lot bought on 9 Jun 2024' })).toBeEnabled();
  });

  it('shows the API’s message when the opening lot’s date is not before the first trade', async () => {
    const { table } = await openLots();
    server.use(
      http.put('/api/holdings/:id/lots/:lotId', () =>
        problemResponse(
          {
            title: 'One or more values are not valid.',
            code: 'portfolio.invalid',
            errors: { acquiredOn: ['The date must be before the earliest trade (10 Jun 2024).'] },
          },
          400,
        ),
      ),
    );
    await userEvent.click(table.getByRole('button', { name: 'Edit the date of the lot bought on 9 Jun 2024' }));

    fireEvent.change(screen.getByLabelText('Acquired on, lot of 9 Jun 2024'), { target: { value: '2024-06-12' } });
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('The date must be before the earliest trade (10 Jun 2024).')).toBeInTheDocument();
  });

  it('explains a refusal to edit a lot that came from trades meanwhile (409) and reloads the lots', async () => {
    const { table } = await openLots();
    let reads = 0;
    server.use(
      http.get('/api/holdings/:id/lots', () => {
        reads += 1;
        return HttpResponse.json(rebuiltLots());
      }),
      http.put('/api/holdings/:id/lots/:lotId', () =>
        problemResponse({ title: 'The lot comes from a trade.', code: 'portfolio.lot_from_trade' }, 409),
      ),
    );
    await userEvent.click(table.getByRole('button', { name: 'Edit the date of the lot bought on 9 Jun 2024' }));
    fireEvent.change(screen.getByLabelText('Acquired on, lot of 9 Jun 2024'), { target: { value: '2024-06-01' } });
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    expect(
      await screen.findByText(
        'This lot comes from your trades, so its date follows them. Change the trades and rebuild the lots instead.',
      ),
    ).toBeInTheDocument();
    await waitFor(() => {
      expect(reads).toBeGreaterThan(0);
    });
  });

  it('still calls the opening lot “before the first trade” when the holding was rebuilt from sells alone', async () => {
    const { table } = await openLots(
      buildLots({
        lots: [
          buildLot({
            id: lot(1),
            acquiredOn: '2024-06-09',
            qty: 5,
            unitCost: 1_500,
            usesDefaultDate: true,
            source: 'import',
          }),
          buildLot({
            id: lot(2),
            acquiredOn: '2024-06-09',
            qty: 5,
            unitCost: 1_500,
            source: 'import',
            status: 'closed',
            closedOn: '2026-09-20',
            closePrice: 1_700,
            estimated: false,
          }),
        ],
      }),
    );

    const open = within(table.getByRole('row', { name: /Default date/ }));
    expect(open.getByText('Before the first trade')).toBeInTheDocument();
    // The closed piece (sold at the real price) is not the opening lot.
    expect(table.getAllByText('Before the first trade')).toHaveLength(1);
  });

  it('does not call a lot “before the first trade” when its closures are only estimates', async () => {
    const { table } = await openLots(buildLots());

    expect(table.queryByText('Before the first trade')).not.toBeInTheDocument();
  });

  it('does not call a lot “before the first trade” when no lot came from trades', async () => {
    const { table } = await openLots(buildLots());

    expect(table.queryByText('Before the first trade')).not.toBeInTheDocument();
    expect(table.getAllByText('Imported').length).toBeGreaterThan(0);
  });
});
