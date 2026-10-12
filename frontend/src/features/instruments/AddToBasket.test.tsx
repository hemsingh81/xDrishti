import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http } from 'msw';
import { createBasketsFake } from '@/test/basketsFake';
import { basketIds, buildPortfolioBasket } from '@/test/fixtures';
import { problemResponse, server } from '@/test/server';
import { smallPage } from '@/test/smallPages';
import { manyInstruments, renderTracking, trackedSample } from './trackingTestkit';

// Lists page by a few rows here instead of 100 (the default of 100 is asserted in useInfiniteRows.test.tsx).
vi.mock('@/shared/lib/useInfiniteRows', async (importOriginal) =>
  (await import('@/test/smallPages')).withSmallPages(await importOriginal()),
);

const bulkButton = (name: string) => screen.getByRole('button', { name });
const rowBox = (symbol: string) => screen.getByRole('checkbox', { name: `Select ${symbol}` });

/** The Instruments screen over the tracking fake and a baskets fake that tracks through it. */
function renderWithBaskets(
  items = trackedSample,
  search = '',
  members: Record<string, string[]> = {},
  portfolio: Parameters<typeof createBasketsFake>[0]['portfolio'] = [],
) {
  const view = renderTracking(items, search);
  const baskets = createBasketsFake({ tracking: view.fake, members, portfolio });
  server.use(...baskets.handlers);
  return { ...view, baskets };
}

describe('Instruments page: Add to basket', () => {
  it('adds the selected rows to the chosen baskets, tracking what is not tracked', async () => {
    const { baskets, fake } = renderWithBaskets();
    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });
    await userEvent.click(rowBox('NSE:RELIANCE'));
    await userEvent.click(rowBox('NSE:TCS'));

    await userEvent.click(bulkButton('Add to basket (2)'));

    const dialog = within(await screen.findByRole('dialog', { name: 'Add 2 instruments to baskets' }));
    expect(await dialog.findByRole('checkbox', { name: /MyChoice/ })).toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Add' })).toBeDisabled();
    await userEvent.click(dialog.getByRole('checkbox', { name: /MyChoice/ }));
    await userEvent.click(dialog.getByRole('checkbox', { name: /Best stocks/ }));
    await userEvent.click(dialog.getByRole('button', { name: 'Add to 2 baskets' }));

    expect(
      await screen.findByText('Added 2 instruments to MyChoice and Best stocks (1 was not tracked and now is)'),
    ).toBeInTheDocument();
    expect(baskets.symbolsIn(basketIds.myChoice)).toEqual(['NSE:RELIANCE', 'NSE:TCS']);
    expect(baskets.symbolsIn(basketIds.bestStocks)).toEqual(['NSE:RELIANCE', 'NSE:TCS']);
    // RELIANCE was not tracked, TCS was: the Tracked tab and counts follow.
    expect(
      fake
        .items()
        .filter((i) => i.tracked.active)
        .map((i) => i.symbol),
    ).toContain('NSE:RELIANCE');
    // The selection is cleared.
    await waitFor(() => {
      expect(bulkButton('Add to basket')).toBeDisabled();
    });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('never lists a portfolio basket as a target', async () => {
    renderWithBaskets(trackedSample, '', {}, [{ basket: buildPortfolioBasket(), holdings: [] }]);
    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });
    await userEvent.click(rowBox('NSE:RELIANCE'));

    await userEvent.click(bulkButton('Add to basket (1)'));

    const dialog = within(await screen.findByRole('dialog'));
    const boxes = await dialog.findAllByRole('checkbox');
    expect(boxes.map((box) => box.closest('label')?.textContent)).toEqual([
      expect.stringContaining('MyChoice'),
      expect.stringContaining('MyLongTerm'),
      expect.stringContaining('Best stocks'),
    ]);
    expect(dialog.queryByRole('checkbox', { name: /Portfolio/ })).not.toBeInTheDocument();
  });

  it('sends more than 500 instruments in calls of at most 500', async () => {
    const { baskets } = renderWithBaskets(manyInstruments(1_150));
    await screen.findByText(`Showing ${String(smallPage)} of 1,150`);
    await userEvent.click(screen.getByRole('checkbox', { name: 'Select all loaded rows' }));
    await userEvent.click(screen.getByRole('button', { name: 'Select all 1,150 results' }));
    await screen.findByText('All 1,150 results are selected');

    await userEvent.click(bulkButton('Add to basket (1,150)'));
    const dialog = within(await screen.findByRole('dialog'));
    await userEvent.click(await dialog.findByRole('checkbox', { name: /MyChoice/ }));
    await userEvent.click(dialog.getByRole('button', { name: 'Add to 1 basket' }));

    expect(await screen.findByText(/^Added 1,150 instruments to MyChoice/)).toBeInTheDocument();
    expect(baskets.calls.add.map((c) => c.instrumentIds.length)).toEqual([500, 500, 150]);
    expect(new Set(baskets.calls.add.flatMap((c) => c.instrumentIds)).size).toBe(1_150);
  });

  it('refuses before sending anything when the selection would not fit, and says by how much', async () => {
    const items = manyInstruments(1_150);
    const { baskets } = renderWithBaskets(items, '', {
      [basketIds.myChoice]: items.slice(0, 1_000).map((i) => i.symbol),
    });
    await screen.findByText(`Showing ${String(smallPage)} of 1,150`);
    await userEvent.click(screen.getByRole('checkbox', { name: 'Select all loaded rows' }));
    await userEvent.click(screen.getByRole('button', { name: 'Select all 1,150 results' }));
    await screen.findByText('All 1,150 results are selected');

    await userEvent.click(bulkButton('Add to basket (1,150)'));
    const dialog = within(await screen.findByRole('dialog'));
    await userEvent.click(await dialog.findByRole('checkbox', { name: /MyChoice/ }));
    await userEvent.click(dialog.getByRole('button', { name: 'Add to 1 basket' }));

    expect(await dialog.findByRole('alert')).toHaveTextContent(
      'MyChoice holds 1,000 and a basket holds at most 2,000 instruments, so up to 1,150 more do not fit. Select 150 fewer.',
    );
    expect(baskets.calls.add).toEqual([]);
    // The choice stays, so the owner can pick another basket or go back and select fewer.
    expect(dialog.getByRole('checkbox', { name: /MyChoice/ })).toBeChecked();
  });

  it('says what was added before an error stopped a long add, and reloads the counts', async () => {
    const { baskets } = renderWithBaskets(manyInstruments(1_150));
    await screen.findByText(`Showing ${String(smallPage)} of 1,150`);
    await userEvent.click(screen.getByRole('checkbox', { name: 'Select all loaded rows' }));
    await userEvent.click(screen.getByRole('button', { name: 'Select all 1,150 results' }));
    await screen.findByText('All 1,150 results are selected');
    await userEvent.click(bulkButton('Add to basket (1,150)'));
    const dialog = within(await screen.findByRole('dialog'));
    expect(await dialog.findByText(/MyChoice/)).toBeInTheDocument();
    await userEvent.click(await dialog.findByRole('checkbox', { name: /MyChoice/ }));
    expect(dialog.getByRole('checkbox', { name: /MyChoice/ })).toHaveAccessibleName(/0 instruments/);
    // The second call of three is refused.
    let calls = 0;
    server.use(
      http.post('/api/baskets/:id/members', () => {
        calls += 1;
        return calls === 2
          ? problemResponse(
              {
                title: 'Too many.',
                code: 'baskets.too_many_members',
                errors: { instrumentIds: ['A basket holds at most 2,000.'] },
              },
              400,
            )
          : undefined;
      }),
    );

    await userEvent.click(dialog.getByRole('button', { name: 'Add to 1 basket' }));

    expect(await dialog.findByRole('alert')).toHaveTextContent(
      'A basket holds at most 2,000. Before the error 500 added to MyChoice; those stay.',
    );
    expect(baskets.calls.add.map((c) => c.instrumentIds.length)).toEqual([500]);
    // The count in the list follows what was applied.
    await waitFor(() => {
      expect(dialog.getByRole('checkbox', { name: /MyChoice/ })).toHaveAccessibleName(/500 instruments/);
    });
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('names the basket that failed and what the earlier ones got when adding to several', async () => {
    const { baskets } = renderWithBaskets();
    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });
    await userEvent.click(rowBox('NSE:RELIANCE'));
    await userEvent.click(bulkButton('Add to basket (1)'));
    const dialog = within(await screen.findByRole('dialog'));
    await userEvent.click(await dialog.findByRole('checkbox', { name: /MyChoice/ }));
    await userEvent.click(dialog.getByRole('checkbox', { name: /Best stocks/ }));
    let calls = 0;
    server.use(
      http.post('/api/baskets/:id/members', () => {
        calls += 1;
        return calls === 2
          ? problemResponse({ title: 'Best stocks is archived.', code: 'baskets.archived' }, 409)
          : undefined;
      }),
    );

    await userEvent.click(dialog.getByRole('button', { name: 'Add to 2 baskets' }));

    expect(await dialog.findByRole('alert')).toHaveTextContent(
      'Best stocks: Best stocks is archived. Before the error 1 added to MyChoice; those stay.',
    );
    expect(baskets.symbolsIn(basketIds.myChoice)).toEqual(['NSE:RELIANCE']);
  });

  it('keeps the dialog open and says why when the server refuses', async () => {
    const { baskets } = renderWithBaskets();
    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });
    await userEvent.click(rowBox('NSE:RELIANCE'));
    baskets.failNext('add', 409, 'baskets.archived', 'The basket is archived.');

    await userEvent.click(bulkButton('Add to basket (1)'));
    const dialog = within(await screen.findByRole('dialog', { name: 'Add 1 instrument to baskets' }));
    await userEvent.click(await dialog.findByRole('checkbox', { name: /MyChoice/ }));
    await userEvent.click(dialog.getByRole('button', { name: 'Add to 1 basket' }));

    expect(await dialog.findByRole('alert')).toHaveTextContent('The basket is archived.');
    await userEvent.click(dialog.getByRole('button', { name: 'Cancel' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(rowBox('NSE:RELIANCE')).toBeChecked();
  });

  it('offers Retry when the baskets cannot be loaded, and says when there are none', async () => {
    renderWithBaskets();
    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });
    await userEvent.click(rowBox('NSE:RELIANCE'));
    let calls = 0;
    server.use(
      http.get('/api/baskets', () => {
        calls += 1;
        return calls === 1
          ? problemResponse({ title: 'The baskets could not be read.', code: 'x' }, 500)
          : Response.json([]);
      }),
    );

    await userEvent.click(bulkButton('Add to basket (1)'));
    const dialog = within(await screen.findByRole('dialog'));
    expect(await dialog.findByText('The baskets could not be read.')).toBeInTheDocument();
    await userEvent.click(dialog.getByRole('button', { name: 'Retry' }));

    expect(await dialog.findByText(/There are no baskets yet/)).toBeInTheDocument();
    expect(dialog.getByRole('link', { name: 'Create one on the Baskets screen.' })).toHaveAttribute('href', '/baskets');
  });
});

describe('Instrument drawer: baskets', () => {
  const tcs = '00000000-0000-4000-8000-000000000202';

  it('shows the baskets of the instrument as links to them', async () => {
    renderWithBaskets(trackedSample, `?id=${tcs}`, {
      [basketIds.myChoice]: ['NSE:TCS'],
      [basketIds.bestStocks]: ['NSE:TCS'],
    });

    const drawer = within(await screen.findByLabelText('Instrument details'));
    const list = await drawer.findByRole('list', { name: 'Baskets of this instrument' });
    expect(within(list).getByRole('link', { name: 'MyChoice' })).toHaveAttribute(
      'href',
      `/baskets?basket=${basketIds.myChoice}`,
    );
    expect(within(list).getByRole('link', { name: 'Best stocks' })).toBeInTheDocument();
  });

  it('says when the instrument is in no basket, and adds it from the drawer', async () => {
    const { baskets } = renderWithBaskets(trackedSample, `?id=${tcs}`);

    const drawer = within(await screen.findByLabelText('Instrument details'));
    expect(await drawer.findByText('Not in any basket.')).toBeInTheDocument();
    await userEvent.click(drawer.getByRole('button', { name: 'Add to basket' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Add NSE:TCS to baskets' }));
    await userEvent.click(await dialog.findByRole('checkbox', { name: /MyLongTerm/ }));
    await userEvent.click(dialog.getByRole('button', { name: 'Add to 1 basket' }));

    expect(await screen.findByText('Added NSE:TCS to MyLongTerm')).toBeInTheDocument();
    expect(baskets.symbolsIn(basketIds.myLongTerm)).toEqual(['NSE:TCS']);
    expect(await drawer.findByRole('link', { name: 'MyLongTerm' })).toBeInTheDocument();
  });

  it('offers Retry when the baskets of the instrument cannot be loaded', async () => {
    renderWithBaskets(trackedSample, `?id=${tcs}`);
    let calls = 0;
    server.use(
      http.get('/api/instruments/:id/baskets', () => {
        calls += 1;
        return calls === 1 ? problemResponse({ title: 'Baskets are down.', code: 'x' }, 500) : Response.json([]);
      }),
    );

    const drawer = within(await screen.findByLabelText('Instrument details'));
    expect(await drawer.findByText('Baskets are down.')).toBeInTheDocument();
    await userEvent.click(drawer.getByRole('button', { name: 'Retry' }));
    expect(await drawer.findByText('Not in any basket.')).toBeInTheDocument();
  });
});
