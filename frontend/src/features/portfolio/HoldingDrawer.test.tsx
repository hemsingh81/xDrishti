import { fireEvent, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import {
  buildLot,
  buildLots,
  buildUnpricedHolding,
  holdingIds,
  lotIds,
  noValueReturns,
  sampleHoldings,
  searchHoldings,
} from '@/test/fixtures';
import { problemResponse, server } from '@/test/server';
import { renderPortfolio } from './testing';
import { pendingResponse } from '@/test/gate';

async function openInfy() {
  const view = renderPortfolio();
  await userEvent.click(await screen.findByRole('gridcell', { name: 'INFY' }));
  const drawer = await screen.findByLabelText('Holding details');
  await within(drawer).findByRole('table', { name: 'Lots' });
  return { ...view, drawer };
}

const lotRow = (drawer: HTMLElement, text: RegExp) => within(drawer).getByRole('row', { name: text });
const setDate = (field: HTMLElement, value: string) => {
  fireEvent.change(field, { target: { value } });
};

describe('Holding drawer', () => {
  it('opens from a row, keeps the holding in the URL and shows its figures', async () => {
    const { router, drawer } = await openInfy();

    expect(router.state.location.search).toBe(`?holding=${holdingIds.infy}`);
    expect(within(drawer).getByRole('heading', { name: 'INFY' })).toBeInTheDocument();
    expect(within(drawer).getByText('Infosys')).toBeInTheDocument();
    expect(within(drawer).getByText('Quantity', { selector: 'dt' }).nextElementSibling).toHaveTextContent('25');
    expect(within(drawer).getByText('Average cost', { selector: 'dt' }).nextElementSibling).toHaveTextContent(
      '₹1,600.00',
    );
    expect(within(drawer).getByText('Profit / loss', { selector: 'dt' }).nextElementSibling).toHaveTextContent(
      '▲+₹2,500',
    );
    expect(within(drawer).getByText('XIRR', { selector: 'dt' }).nextElementSibling).toHaveTextContent('—');
    expect(within(drawer).getByText('ISIN', { selector: 'dt' }).nextElementSibling).toHaveTextContent('INE009A01021');
  });

  it('reopens from the URL', async () => {
    renderPortfolio(`?holding=${holdingIds.tcs}`);

    const drawer = await screen.findByLabelText('Holding details');
    expect(await within(drawer).findByRole('table', { name: 'Lots' })).toBeInTheDocument();
    expect(within(drawer).getByRole('heading', { name: 'TCS' })).toBeInTheDocument();
  });

  it('closes with the close button and takes the holding out of the URL', async () => {
    const { router, drawer } = await openInfy();

    await userEvent.click(within(drawer).getByRole('button', { name: 'Close details' }));

    await vi.waitFor(() => {
      expect(router.state.location.search).toBe('');
    });
  });

  it('flags holdings that are closed or not in the master list', async () => {
    renderPortfolio(`?holding=${holdingIds.smallco}`);

    const drawer = await screen.findByLabelText('Holding details');
    expect(await within(drawer).findByText('Not in master list')).toBeInTheDocument();
  });

  it('lists the lots with their own P&L and XIRR, default-date and estimated flags', async () => {
    const { drawer } = await openInfy();

    const first = lotRow(drawer, /1 Jan 2025/);
    expect(within(first).getByText('Default date')).toBeInTheDocument();
    expect(within(first).getByText('+₹3,000')).toBeInTheDocument();
    expect(within(first).getByText('+9.10%')).toBeInTheDocument();
    expect(within(first).getByText('₹1,500.00')).toBeInTheDocument();
    expect(within(first).getByText('Imported')).toBeInTheDocument();

    const added = lotRow(drawer, /5 Oct 2026/);
    expect(within(added).getByText('−₹500')).toBeInTheDocument();
    expect(within(added).getByText('—')).toBeInTheDocument(); // too young for an XIRR
    expect(within(added).getByText('Added by sync')).toBeInTheDocument();
    expect(within(added).queryByText('Default date')).not.toBeInTheDocument();

    const closed = lotRow(drawer, /10 Jun 2024/);
    expect(within(closed).getByText('Closed 20 Sept 2026 at ₹1,650.00')).toBeInTheDocument();
    expect(within(closed).getByText('Estimated')).toBeInTheDocument();
    expect(within(drawer).getByText(/Values as of 11 Oct 2026/)).toBeInTheDocument();
  });

  it('shows an error with Retry when the lots cannot be loaded', async () => {
    let fail = true;
    server.use(
      http.get('/api/holdings/:id/lots', () =>
        fail ? new HttpResponse(null, { status: 500 }) : HttpResponse.json(buildLots()),
      ),
    );
    renderPortfolio(`?holding=${holdingIds.infy}`);

    const drawer = await screen.findByLabelText('Holding details');
    expect(await within(drawer).findByText('The lots could not be loaded.')).toBeInTheDocument();
    fail = false;
    await userEvent.click(within(drawer).getByRole('button', { name: 'Retry' }));
    expect(await within(drawer).findByRole('table', { name: 'Lots' })).toBeInTheDocument();
  });

  it('shows a progress indicator while the lots load', async () => {
    server.use(http.get('/api/holdings/:id/lots', () => pendingResponse()));
    renderPortfolio(`?holding=${holdingIds.infy}`);

    const drawer = await screen.findByLabelText('Holding details');
    expect(await within(drawer).findByRole('progressbar', { name: 'Loading lots' })).toBeInTheDocument();
  });
});

describe('Holding drawer: editing a lot’s date', () => {
  const editButton = (drawer: HTMLElement, day: string) =>
    within(drawer).getByRole('button', { name: `Edit the date of the lot bought on ${day}` });
  const field = (drawer: HTMLElement) => within(drawer).getByLabelText('Acquired on, lot of 1 Jan 2025');

  it('saves a valid date, reloads the lots and the list, and closes the editor', async () => {
    const puts: { path: string; body: unknown }[] = [];
    let lotLoads = 0;
    let listLoads = 0;
    server.use(
      http.get('/api/holdings/:id/lots', () => {
        lotLoads += 1;
        return HttpResponse.json(buildLots());
      }),
      http.get('/api/holdings', ({ request }) => {
        listLoads += 1;
        return HttpResponse.json(searchHoldings(sampleHoldings, new URL(request.url).searchParams));
      }),
      http.put('/api/holdings/:id/lots/:lotId', async ({ request, params }) => {
        puts.push({ path: `${String(params.id)}/${String(params.lotId)}`, body: await request.json() });
        return HttpResponse.json(buildLot({ id: lotIds.first, acquiredOn: '2024-02-10' }));
      }),
    );
    const { drawer } = await openInfy();
    const before = lotLoads;
    const listBefore = listLoads;

    await userEvent.click(editButton(drawer, '1 Jan 2025'));
    expect(field(drawer)).toHaveValue('2025-01-01');
    setDate(field(drawer), '2024-02-10');
    await userEvent.click(within(drawer).getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('Acquisition date updated.')).toBeInTheDocument();
    expect(puts).toEqual([{ path: `${holdingIds.infy}/${lotIds.first}`, body: { acquiredOn: '2024-02-10' } }]);
    await vi.waitFor(() => {
      expect(lotLoads).toBeGreaterThan(before);
      expect(listLoads).toBeGreaterThan(listBefore);
    });
    expect(within(drawer).queryByLabelText('Acquired on, lot of 1 Jan 2025')).not.toBeInTheDocument();
  });

  it.each([
    ['', 'Enter a date.'],
    ['2099-01-01', 'The date cannot be in the future.'],
    ['1985-06-01', 'The date cannot be before 1 Jan 1990.'],
  ])('refuses the date "%s" without asking the server', async (value, message) => {
    let puts = 0;
    server.use(
      http.put('/api/holdings/:id/lots/:lotId', () => {
        puts += 1;
        return HttpResponse.json(buildLot());
      }),
    );
    const { drawer } = await openInfy();

    await userEvent.click(editButton(drawer, '1 Jan 2025'));
    setDate(field(drawer), value);
    await userEvent.click(within(drawer).getByRole('button', { name: 'Save' }));

    expect(await within(drawer).findByText(message)).toBeInTheDocument();
    expect(field(drawer)).toBeInvalid();
    expect(puts).toBe(0);
  });

  it('clears the message when the date is changed', async () => {
    const { drawer } = await openInfy();
    await userEvent.click(editButton(drawer, '1 Jan 2025'));
    setDate(field(drawer), '2099-01-01');
    await userEvent.click(within(drawer).getByRole('button', { name: 'Save' }));
    await within(drawer).findByText('The date cannot be in the future.');

    setDate(field(drawer), '2024-02-10');

    expect(within(drawer).queryByText('The date cannot be in the future.')).not.toBeInTheDocument();
  });

  it('shows the message the API gives for the date', async () => {
    server.use(
      http.put('/api/holdings/:id/lots/:lotId', () =>
        problemResponse(
          {
            title: 'Some values are not valid.',
            code: 'portfolio.invalid',
            errors: { acquiredOn: ['The date cannot be after the day the lot was closed.'] },
          },
          400,
        ),
      ),
    );
    const { drawer } = await openInfy();

    await userEvent.click(editButton(drawer, '1 Jan 2025'));
    setDate(field(drawer), '2024-02-10');
    await userEvent.click(within(drawer).getByRole('button', { name: 'Save' }));

    expect(await within(drawer).findByText('The date cannot be after the day the lot was closed.')).toBeInTheDocument();
    expect(field(drawer)).toBeInvalid();
  });

  it('shows a general failure when the save does not go through', async () => {
    server.use(
      http.put('/api/holdings/:id/lots/:lotId', () =>
        problemResponse({ title: 'The lot does not exist.', code: 'portfolio.not_found' }, 404),
      ),
    );
    const { drawer } = await openInfy();

    await userEvent.click(editButton(drawer, '1 Jan 2025'));
    setDate(field(drawer), '2024-02-10');
    await userEvent.click(within(drawer).getByRole('button', { name: 'Save' }));

    expect(await within(drawer).findByText('The lot does not exist.')).toBeInTheDocument();
  });

  it('says the lot was changed meanwhile and loads it again', async () => {
    let lotLoads = 0;
    server.use(
      http.get('/api/holdings/:id/lots', () => {
        lotLoads += 1;
        return HttpResponse.json(buildLots());
      }),
      http.put('/api/holdings/:id/lots/:lotId', () =>
        problemResponse({ title: 'The lot was changed.', code: 'portfolio.conflict' }, 409),
      ),
    );
    const { drawer } = await openInfy();
    const before = lotLoads;

    await userEvent.click(editButton(drawer, '1 Jan 2025'));
    setDate(field(drawer), '2024-02-10');
    await userEvent.click(within(drawer).getByRole('button', { name: 'Save' }));

    expect(
      await within(drawer).findByText('The lot was changed or closed meanwhile. Reload and try again.'),
    ).toBeInTheDocument();
    await vi.waitFor(() => {
      expect(lotLoads).toBeGreaterThan(before);
    });
  });

  it('cancels without saving', async () => {
    const { drawer } = await openInfy();

    await userEvent.click(editButton(drawer, '1 Jan 2025'));
    await userEvent.click(within(drawer).getByRole('button', { name: 'Cancel' }));

    expect(within(drawer).queryByLabelText('Acquired on, lot of 1 Jan 2025')).not.toBeInTheDocument();
    expect(within(drawer).getByText('Default date')).toBeInTheDocument();
  });
});

describe('Holding drawer: unpriced holdings', () => {
  it('shows "No price yet" on the holding and on its lots, with dashes instead of values', async () => {
    const holding = buildUnpricedHolding();
    server.use(
      http.get('/api/holdings', ({ request }) =>
        HttpResponse.json(searchHoldings([holding], new URL(request.url).searchParams)),
      ),
      http.get('/api/holdings/:id/lots', () =>
        HttpResponse.json(
          buildLots({
            holdingId: holding.id,
            symbol: holding.symbol,
            lots: [buildLot({ qty: 10, unitCost: 200, returns: noValueReturns({ invested: 2_000 }) })],
          }),
        ),
      ),
    );
    renderPortfolio(`?holding=${holding.id}`);

    const drawer = await screen.findByLabelText('Holding details');
    const lots = await within(drawer).findByRole('table', { name: 'Lots' });
    expect(within(lots).getByText('No price yet')).toBeInTheDocument();
    expect(within(lots).getAllByRole('img', { name: /No price yet/ }).length).toBeGreaterThan(0);
    expect(within(drawer).getByText('Value', { selector: 'dt' }).nextElementSibling).toHaveTextContent('—');
    expect(within(drawer).getByText('Profit / loss', { selector: 'dt' }).nextElementSibling).toHaveTextContent('—');
    // Both reasons can be reached with the keyboard.
    expect(
      within(drawer)
        .getAllByRole('img', { name: /No price yet/ })
        .some((e) => e.tabIndex === 0),
    ).toBe(true);
  });
});
