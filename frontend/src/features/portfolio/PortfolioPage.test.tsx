import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import {
  buildAccountDetail,
  buildBucket,
  buildBuckets,
  buildHolding,
  buildUnpricedHolding,
  buildSummary,
  buildReturns,
  buildSyncStatus,
  ids,
  noValueReturns,
  sampleHoldings,
  searchHoldings,
  summaryOf,
  withConnection,
  buildConnection,
} from '@/test/fixtures';
import { setNow } from '@/test/clock';
import { scrollToEnd } from '@/test/intersection';
import { problemResponse, server } from '@/test/server';
import { smallPage, threePages } from '@/test/smallPages';
import { noHoldingsHandlers, renderPortfolio, twoPortfolioAccounts } from './testing';

// Lists page by a few rows here instead of 100 (the default of 100 is asserted in useInfiniteRows.test.tsx).
vi.mock('@/shared/lib/useInfiniteRows', async (importOriginal) =>
  (await import('@/test/smallPages')).withSmallPages(await importOriginal()),
);

const twoPages = smallPage * 1.5; // the second page half full
const shown = (count: number, total: number) => `Showing ${String(count)} of ${String(total)}`;
const symbolOf = (n: number) => `S${String(n).padStart(3, '0')}`;
const gridName = 'Holdings';

const symbolsShown = () =>
  within(screen.getByRole('grid', { name: gridName }))
    .getAllByRole('row')
    .slice(1)
    .map((row) =>
      within(row)
        .getAllByRole('gridcell')[0]
        ?.textContent.replace(/(Not in master list|Closed)$/, ''),
    );

const expectSymbols = (symbols: string[]) =>
  vi.waitFor(() => {
    expect(symbolsShown()).toEqual(symbols);
  });

/** The reason of a missing figure in the totals: the one that can be reached with the Tab key (grid rows have their own). */
const tileTrigger = (name: RegExp) => {
  const trigger = screen.getAllByRole('img', { name }).find((element) => element.getAttribute('tabindex') === '0');
  if (!trigger) throw new Error(`No keyboard-reachable reason matching ${String(name)}`);
  return trigger;
};

/** The cells of one holding's row, in column order, as text. */
const rowTexts = async (symbol: string) => {
  const row = await screen.findByRole('row', { name: new RegExp(`^${symbol}\\b`) });
  return within(row)
    .getAllByRole('gridcell')
    .map((cell) => cell.textContent);
};

describe('Portfolio page: totals', () => {
  it('shows invested, value, P&L with sign and arrow, XIRR, realised and the counts', async () => {
    renderPortfolio();

    expect(await screen.findByText('₹1,85,000')).toBeInTheDocument();
    expect(screen.getByText('₹2,03,400')).toBeInTheDocument();
    expect(screen.getByText('as of 11 Oct 2026')).toBeInTheDocument();
    expect(screen.getByText('+₹18,400')).toHaveTextContent('▲+₹18,400');
    expect(screen.getByText('+9.95%')).toHaveTextContent('▲+9.95%');
    expect(screen.getByText('+11.30%')).toHaveTextContent('▲+11.30%');
    expect(screen.getByText('Realised (est.)')).toBeInTheDocument();
    expect(screen.getByText('+₹800')).toBeInTheDocument();
    expect(screen.getByText('1 closed · 1 not in master list')).toBeInTheDocument();
    expect(screen.getByText('Dividends, charges and corporate actions are not included.')).toBeInTheDocument();
  });

  it('shows a loss with a minus sign and a down arrow, never by colour alone', async () => {
    server.use(
      http.get('/api/holdings/summary', () =>
        HttpResponse.json(
          buildSummary({
            returns: buildReturns({ invested: 100_000, value: 88_000, pnl: -12_000, pnlPct: -12, xirr: -9.4 }),
          }),
        ),
      ),
    );
    renderPortfolio();

    expect(await screen.findByText('−₹12,000')).toHaveTextContent('▼−₹12,000');
    expect(screen.getByText('−12.00%')).toHaveTextContent('▼−12.00%');
    expect(screen.getByText('−9.40%')).toHaveTextContent('▼−9.40%');
  });

  it.each([
    ['insufficient', '—', 'Needs 30+ days of history', /first purchase at least 30 days before today/],
    ['n_m', 'n/m', 'Not meaningful (beyond ±1,000 %)', /beyond ±1,000 %/],
    ['no_data', '—', 'No cash flows yet', /no cash flows yet/],
    ['no_price', '—', 'No price yet', /No price yet, so there is no current value and no XIRR/],
  ])('explains an XIRR that is not available (%s)', async (xirrState, label, shortText, longText) => {
    server.use(
      http.get('/api/holdings/summary', () =>
        HttpResponse.json(buildSummary({ returns: buildReturns({ xirr: null, xirrState }) })),
      ),
    );
    renderPortfolio();

    expect(await screen.findByText(shortText)).toBeInTheDocument();
    const trigger = tileTrigger(longText);
    expect(trigger).toHaveTextContent(label);
    await userEvent.hover(trigger);
    expect(await screen.findByRole('tooltip')).toHaveTextContent(longText);
  });

  it('opens the reason of a missing figure from the keyboard', async () => {
    server.use(
      http.get('/api/holdings/summary', () =>
        HttpResponse.json(buildSummary({ returns: buildReturns({ xirr: null, xirrState: 'insufficient' }) })),
      ),
    );
    renderPortfolio();
    await screen.findByText('Needs 30+ days of history');
    const trigger = tileTrigger(/first purchase at least 30 days/);

    for (let presses = 0; presses < 30 && document.activeElement !== trigger; presses += 1) {
      await userEvent.tab();
    }

    expect(trigger).toHaveFocus();
    expect(await screen.findByRole('tooltip')).toHaveTextContent(/first purchase at least 30 days/);
  });

  it('shows no value, P&L or percentage when nothing in the group has a price, and says why', async () => {
    server.use(
      http.get('/api/holdings/summary', () =>
        HttpResponse.json(buildSummary({ returns: noValueReturns({ invested: 2_000 }), unpricedCount: 1 })),
      ),
    );
    renderPortfolio();

    expect(
      await screen.findByText('1 holding without a price is left out of value, P&L and XIRR.'),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('img', { name: /No price yet, so there is no current value, profit or loss/ }),
    ).toHaveTextContent('—');
  });

  it('counts the holdings left out for lack of a price', async () => {
    server.use(http.get('/api/holdings/summary', () => HttpResponse.json(buildSummary({ unpricedCount: 3 }))));
    renderPortfolio();

    expect(
      await screen.findByText('3 holdings without a price are left out of value, P&L and XIRR.'),
    ).toBeInTheDocument();
  });

  it('says nothing about prices when every holding has one', async () => {
    renderPortfolio();

    await screen.findByText('₹1,85,000');
    expect(screen.queryByText(/without a price/)).not.toBeInTheDocument();
  });

  it('counts the lots on the default date and sorts the oldest purchases first to find them', async () => {
    const { router } = renderPortfolio();

    const chip = await screen.findByRole('button', { name: '2 lots use the default date' });
    await userEvent.click(chip);

    expect(router.state.location.search).toBe('?sort=acquiredOn');
  });

  it('says "1 lot uses" for a single lot and shows no chip when there are none', async () => {
    server.use(http.get('/api/holdings/summary', () => HttpResponse.json(buildSummary({ defaultDateLots: 1 }))));
    const { unmount } = renderPortfolio();
    expect(await screen.findByRole('button', { name: '1 lot uses the default date' })).toBeInTheDocument();
    unmount();

    server.use(http.get('/api/holdings/summary', () => HttpResponse.json(buildSummary({ defaultDateLots: 0 }))));
    renderPortfolio();
    await screen.findByText('₹1,85,000');
    expect(screen.queryByRole('button', { name: /default date/ })).not.toBeInTheDocument();
  });

  it('shows a loading indicator, then an error with Retry when the totals fail', async () => {
    let fail = true;
    server.use(
      http.get('/api/holdings/summary', () =>
        fail ? new HttpResponse(null, { status: 500 }) : HttpResponse.json(buildSummary()),
      ),
    );
    renderPortfolio();

    expect(await screen.findByText('The totals could not be loaded.')).toBeInTheDocument();
    fail = false;
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByText('₹1,85,000')).toBeInTheDocument();
  });

  it('shows a progress indicator while the totals load', async () => {
    server.use(http.get('/api/holdings/summary', () => new Promise(() => undefined)));
    renderPortfolio();

    expect(await screen.findByRole('progressbar', { name: 'Loading totals' })).toBeInTheDocument();
  });
});

describe('Portfolio page: holdings', () => {
  it('lists the open holdings with every column, flags and dashes', async () => {
    setNow('2026-10-11T10:30:00Z'); // the day of the prices: no date under the day change
    renderPortfolio();

    await screen.findByRole('gridcell', { name: 'RELIANCE' });
    expect(screen.getByRole('heading', { level: 1, name: 'Portfolio' })).toBeInTheDocument();
    expect(symbolsShown()).toEqual(['INFY', 'RELIANCE', 'SMALLCO', 'TCS']); // the closed one is hidden by default
    for (const header of [
      'Symbol',
      'Name',
      'Qty',
      'Avg cost',
      'LTP',
      'Day change',
      'Invested',
      'Value',
      'P&L',
      'P&L %',
      'XIRR',
      'Acquired',
      'Bucket',
    ]) {
      expect(screen.getByRole('columnheader', { name: header })).toBeInTheDocument();
    }
    expect(screen.queryByRole('columnheader', { name: 'Account' })).not.toBeInTheDocument();

    expect(await rowTexts('RELIANCE')).toEqual([
      'RELIANCE',
      'Reliance Industries',
      '40',
      '₹2,500.00',
      '₹2,902.50',
      '▲+₹900▲+0.78%',
      '₹1,00,000',
      '₹1,16,100',
      '▲+₹16,100',
      '▲+16.10%',
      '▲+14.20%',
      '5 Mar 20241 lot',
      'Test Dhan Main · Portfolio',
    ]);
  });

  it('marks losses, dashes, default dates and holdings that are not in the master list', async () => {
    renderPortfolio();

    const tcs = await rowTexts('TCS');
    expect(tcs[8]).toBe('▼−₹1,200');
    expect(tcs[9]).toBe('▼−3.00%');
    expect(tcs[10]).toBe('▼−2.50%');

    const infy = await rowTexts('INFY');
    expect(infy[10]).toBe('—'); // too young for an XIRR
    expect(infy[11]).toBe('1 Jan 2025Default date2 lots');

    const small = await rowTexts('SMALLCO');
    expect(small[0]).toBe('SMALLCONot in master list');
    expect(small[10]).toBe('n/m'); // beyond ±1,000 %
  });

  it('shows an unpriced holding as "No price yet", with dashes instead of a loss of 100 %', async () => {
    server.use(
      http.get('/api/holdings', ({ request }) =>
        HttpResponse.json(
          searchHoldings([...sampleHoldings, buildUnpricedHolding()], new URL(request.url).searchParams),
        ),
      ),
    );
    renderPortfolio();

    const cells = await rowTexts('NOPRICE');
    expect(cells[0]).toBe('NOPRICENo price yet');
    expect(cells[2]).toBe('10');
    expect(cells[4]).toBe('—'); // last price
    expect(cells[5]).toBe('—'); // day change
    expect(cells.slice(6, 11)).toEqual(['₹2,000', '—', '—', '—', '—']); // invested, value, P&L, P&L %, XIRR
    expect(cells.join(' ')).not.toMatch(/100\.00%|−₹2,000/);
    const row = screen.getByRole('row', { name: /^NOPRICE/ });
    expect(within(row).getAllByRole('img', { name: /No price yet/ }).length).toBeGreaterThan(0);
  });

  it('shows the price time as a tooltip on the last price', async () => {
    renderPortfolio();

    const price = await screen.findAllByText('₹2,902.50');
    await userEvent.hover(price[0]!);
    expect(await screen.findByRole('tooltip')).toHaveTextContent(/Price as of .*2026/);
  });

  it('sorts through the API when a column header is pressed, and keeps the sort in the URL', async () => {
    const { router } = renderPortfolio();
    await screen.findByRole('gridcell', { name: 'RELIANCE' });

    await userEvent.click(within(screen.getByRole('columnheader', { name: 'Value' })).getByRole('button'));

    await vi.waitFor(() => {
      expect(router.state.location.search).toBe('?sort=value');
    });
    await expectSymbols(['SMALLCO', 'TCS', 'INFY', 'RELIANCE']);
    await userEvent.click(within(screen.getByRole('columnheader', { name: 'Value' })).getByRole('button'));
    await expectSymbols(['RELIANCE', 'INFY', 'TCS', 'SMALLCO']);
    expect(router.state.location.search).toBe('?sort=-value');
  });

  it('searches symbol and name after the typing stops, and clears the search', async () => {
    const { router } = renderPortfolio();
    await screen.findByRole('gridcell', { name: 'RELIANCE' });

    await userEvent.type(screen.getByRole('searchbox', { name: 'Search holdings' }), 'consult');

    await expectSymbols(['TCS']);
    expect(router.state.location.search).toBe('?q=consult');
    await userEvent.click(screen.getByRole('button', { name: 'Clear search' }));
    await expectSymbols(['INFY', 'RELIANCE', 'SMALLCO', 'TCS']);
    expect(router.state.location.search).toBe('');
  });

  it('switches between open, closed and all holdings', async () => {
    const { router } = renderPortfolio();
    await screen.findByRole('gridcell', { name: 'RELIANCE' });
    expect(screen.getByRole('button', { name: 'Open', pressed: true })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Closed' }));
    await expectSymbols(['OLDCO']);
    expect(router.state.location.search).toBe('?status=closed');
    expect(await rowTexts('OLDCO')).toContain('OLDCOClosed');

    await userEvent.click(screen.getByRole('button', { name: 'All' }));
    await expectSymbols(['INFY', 'OLDCO', 'RELIANCE', 'SMALLCO', 'TCS']);
    expect(router.state.location.search).toBe('?status=all');
  });

  it('reads the whole view from the URL', async () => {
    renderPortfolio('?status=all&q=ltd&sort=-qty');

    await expectSymbols(['SMALLCO', 'OLDCO']);
    expect(screen.getByRole('button', { name: 'All', pressed: true })).toBeInTheDocument();
    expect(screen.getByRole('searchbox', { name: 'Search holdings' })).toHaveValue('ltd');
    expect(screen.getByRole('columnheader', { name: 'Qty' })).toHaveAttribute('aria-sort', 'descending');
  });

  it('shows "Showing N of total" and loads the next page when the list is scrolled to its end', async () => {
    const many = Array.from({ length: threePages }, (_, n) =>
      buildHolding({
        id: `00000000-0000-4000-8000-00000000${String(7000 + n)}`,
        symbol: `S${String(n).padStart(3, '0')}`,
      }),
    );
    server.use(
      http.get('/api/holdings', ({ request }) =>
        HttpResponse.json(searchHoldings(many, new URL(request.url).searchParams)),
      ),
    );
    const { router } = renderPortfolio();

    await screen.findByText(shown(smallPage, threePages));
    expect(screen.queryByRole('button', { name: 'Go to next page' })).not.toBeInTheDocument();
    scrollToEnd();
    await screen.findByText(shown(smallPage * 2, threePages));
    expect(screen.getByRole('gridcell', { name: symbolOf(smallPage * 2 - 1) })).toBeInTheDocument();
    expect(screen.getByRole('gridcell', { name: 'S000' })).toBeInTheDocument();
    scrollToEnd();
    await screen.findByText(shown(threePages, threePages));
    expect(router.state.location.search).toBe(''); // no page or size in the URL
  });

  it('starts a new list at the top when the sort order changes, and keeps the totals', async () => {
    const many = Array.from({ length: twoPages }, (_, n) =>
      buildHolding({
        id: `00000000-0000-4000-8000-00000000${String(7000 + n)}`,
        symbol: `S${String(n).padStart(3, '0')}`,
      }),
    );
    server.use(
      http.get('/api/holdings', ({ request }) =>
        HttpResponse.json(searchHoldings(many, new URL(request.url).searchParams)),
      ),
    );
    renderPortfolio();
    await screen.findByText(shown(smallPage, twoPages));
    scrollToEnd();
    await screen.findByText(shown(twoPages, twoPages));

    await userEvent.click(screen.getByRole('button', { name: 'Symbol' }));

    await screen.findByRole('gridcell', { name: symbolOf(twoPages - 1) });
    expect(screen.getByText(shown(smallPage, twoPages))).toBeInTheDocument();
    expect(screen.queryByRole('gridcell', { name: symbolOf(twoPages - smallPage - 1) })).not.toBeInTheDocument();
    expect(screen.getByText('Current value')).toBeInTheDocument(); // the summary tiles stay
  });

  it('keeps the loaded rows and offers a retry when a further page cannot be loaded', async () => {
    const many = Array.from({ length: twoPages }, (_, n) =>
      buildHolding({
        id: `00000000-0000-4000-8000-00000000${String(7000 + n)}`,
        symbol: `S${String(n).padStart(3, '0')}`,
      }),
    );
    let fail = true;
    server.use(
      http.get('/api/holdings', ({ request }) => {
        const params = new URL(request.url).searchParams;
        return params.get('page') === '2' && fail
          ? problemResponse({ title: 'Holdings are unavailable.', code: 'x.y' }, 500)
          : HttpResponse.json(searchHoldings(many, params));
      }),
    );
    renderPortfolio();
    await screen.findByText(shown(smallPage, twoPages));

    scrollToEnd();

    expect(await screen.findByText('Could not load more rows')).toBeInTheDocument();
    expect(screen.getByRole('gridcell', { name: symbolOf(smallPage - 1) })).toBeInTheDocument();
    fail = false;
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
    await screen.findByText(shown(twoPages, twoPages));
  });

  it('sends the sort, search, status and a page of rows to the API', async () => {
    const seen: URLSearchParams[] = [];
    server.use(
      http.get('/api/holdings', ({ request }) => {
        const params = new URL(request.url).searchParams;
        seen.push(params);
        return HttpResponse.json(searchHoldings(sampleHoldings, params));
      }),
    );
    renderPortfolio('?status=closed&q=old&sort=-pnl&page=3&size=25');

    await screen.findByRole('gridcell', { name: /^OLDCO/ });
    expect(Object.fromEntries(seen.at(-1) ?? [])).toEqual({
      status: 'closed',
      q: 'old',
      sort: '-pnl',
      page: '1', // the page and size of an old link are ignored
      pageSize: String(smallPage),
    });
  });

  it('shows skeleton rows on the first load and an error with Retry when the list fails', async () => {
    server.use(http.get('/api/holdings', () => new Promise(() => undefined)));
    const { unmount } = renderPortfolio();
    expect(await screen.findByRole('grid', { name: gridName })).toHaveAttribute('aria-busy', 'true');
    unmount();

    let fail = true;
    server.use(
      http.get('/api/holdings', ({ request }) =>
        fail
          ? problemResponse({ title: 'Holdings are unavailable.', code: 'x.y' }, 500)
          : HttpResponse.json(searchHoldings(sampleHoldings, new URL(request.url).searchParams)),
      ),
    );
    renderPortfolio();
    expect(await screen.findByText('Holdings are unavailable.')).toBeInTheDocument();
    fail = false;
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByRole('gridcell', { name: 'RELIANCE' })).toBeInTheDocument();
  });

  it('says so when nothing matches, and clears the filters', async () => {
    renderPortfolio('?q=zzz');

    expect(await screen.findByText('No holdings match these filters')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Clear filters' }));
    expect(await screen.findByRole('gridcell', { name: 'RELIANCE' })).toBeInTheDocument();
  });
});

describe('Portfolio page: accounts and buckets', () => {
  it('lists the accounts with the Portfolio role and adds an Account column for all of them', async () => {
    server.use(twoPortfolioAccounts());
    const { router } = renderPortfolio();
    await screen.findByRole('gridcell', { name: 'RELIANCE' });
    expect(screen.getByRole('columnheader', { name: 'Account' })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('combobox', { name: 'Account' }));
    expect(screen.getAllByRole('option').map((o) => o.textContent)).toEqual([
      expect.stringContaining('Test Dhan Main'),
      expect.stringContaining('Test Dhan Swing'),
    ]);
    await userEvent.click(screen.getByRole('option', { name: /Test Dhan Swing/ }));

    await vi.waitFor(() => {
      expect(router.state.location.search).toBe(`?account=${ids.swing}`);
    });
    expect(screen.queryByRole('columnheader', { name: 'Account' })).not.toBeInTheDocument();
  });

  it('offers only accounts with the Portfolio role', async () => {
    renderPortfolio();
    await screen.findByRole('gridcell', { name: 'RELIANCE' });

    await userEvent.click(screen.getByRole('combobox', { name: 'Account' }));

    expect(screen.getAllByRole('option')).toHaveLength(1);
    expect(screen.queryByRole('option', { name: /Swing/ })).not.toBeInTheDocument();
  });

  it('treats a link to an account without the Portfolio role as all accounts', async () => {
    const seen: (string | null)[] = [];
    server.use(
      http.get('/api/holdings', ({ request }) => {
        seen.push(new URL(request.url).searchParams.get('accountId'));
        return HttpResponse.json(searchHoldings(sampleHoldings, new URL(request.url).searchParams));
      }),
    );
    renderPortfolio(`?account=${ids.swing}`);

    await screen.findByRole('gridcell', { name: 'RELIANCE' });
    expect(seen.every((id) => id === null)).toBe(true);
  });

  it('filters by bucket', async () => {
    const custom = buildBucket({ id: '00000000-0000-4000-8000-0000000000b2', name: 'Retirement' });
    server.use(http.get('/api/portfolio-buckets', () => HttpResponse.json([...buildBuckets(), custom])));
    const seen: (string | null)[] = [];
    server.use(
      http.get('/api/holdings', ({ request }) => {
        seen.push(new URL(request.url).searchParams.get('bucketId'));
        return HttpResponse.json(searchHoldings(sampleHoldings, new URL(request.url).searchParams));
      }),
    );
    const { router } = renderPortfolio();
    await screen.findByRole('gridcell', { name: 'RELIANCE' });

    await userEvent.click(screen.getByRole('combobox', { name: 'Bucket' }));
    await userEvent.click(await screen.findByRole('option', { name: 'Retirement' }));

    await vi.waitFor(() => {
      expect(router.state.location.search).toBe(`?bucket=${custom.id}`);
    });
    await vi.waitFor(() => {
      expect(seen.at(-1)).toBe(custom.id);
    });
  });
});

describe('Portfolio page: empty and blocked states', () => {
  it('points to the account’s Portfolio settings when there are no holdings at all', async () => {
    server.use(...noHoldingsHandlers());
    renderPortfolio();

    expect(await screen.findByText('No holdings yet')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open Portfolio settings' })).toHaveAttribute(
      'href',
      `/accounts?account=${ids.main}&tab=portfolio`,
    );
  });

  it('has no import or auto refresh control, only Sync now', async () => {
    renderPortfolio();

    await screen.findByRole('gridcell', { name: 'RELIANCE' });
    expect(screen.getByRole('button', { name: 'Sync now' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /import/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Auto refresh' })).not.toBeInTheDocument();
  });

  it('points to the Accounts screen without a chosen account when several accounts could be meant', async () => {
    server.use(...noHoldingsHandlers(), twoPortfolioAccounts());
    renderPortfolio();

    expect(await screen.findByText('No holdings yet')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open Portfolio settings' })).toHaveAttribute(
      'href',
      '/accounts?tab=portfolio',
    );
    expect(screen.getByRole('button', { name: 'Sync now' })).toBeDisabled();
  });

  it('says no account has the Portfolio role when none has', async () => {
    const off = buildAccountDetail({ roles: { ...buildAccountDetail().roles, portfolio: { enabled: false } } });
    server.use(http.get('/api/accounts', () => HttpResponse.json([summaryOf(off)])));
    renderPortfolio();

    expect(await screen.findByText('No account has the Portfolio role yet')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open Accounts' })).toHaveAttribute('href', '/accounts?tab=roles');
    expect(screen.queryByRole('grid', { name: gridName })).not.toBeInTheDocument();
  });

  it('warns that the account is not connected, with a link to its Connection tab, and blocks sync', async () => {
    const expired = withConnection(buildAccountDetail(), buildConnection({ status: 'expired' }));
    server.use(http.get('/api/accounts', () => HttpResponse.json([summaryOf(expired)])));
    renderPortfolio();

    expect(await screen.findByText(/Connect the account first\./)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open the Connection tab' })).toHaveAttribute(
      'href',
      `/accounts?account=${ids.main}&tab=connection`,
    );
    expect(screen.getByRole('button', { name: 'Sync now' })).toBeDisabled();
    // What is already in the system stays visible.
    expect(await screen.findByRole('gridcell', { name: 'RELIANCE' })).toBeInTheDocument();
  });

  it('shows an error with Retry when the accounts cannot be loaded', async () => {
    let fail = true;
    server.use(
      http.get('/api/accounts', () =>
        fail ? new HttpResponse(null, { status: 500 }) : HttpResponse.json([summaryOf(buildAccountDetail())]),
      ),
    );
    renderPortfolio();

    await userEvent.click(await screen.findByRole('button', { name: 'Retry' }));
    fail = false;
    await userEvent.click(await screen.findByRole('button', { name: 'Retry' }));
    expect(await screen.findByRole('gridcell', { name: 'RELIANCE' })).toBeInTheDocument();
  });

  it('keeps the sync status out of the way when it fails to load', async () => {
    server.use(http.get('/api/accounts/:id/holdings/sync-status', () => new HttpResponse(null, { status: 500 })));
    renderPortfolio();

    expect(await screen.findByText('The sync status could not be loaded.')).toBeInTheDocument();
    expect(await screen.findByRole('gridcell', { name: 'RELIANCE' })).toBeInTheDocument();
  });
});

describe('Portfolio page: sync status line', () => {
  it('shows the last sync in words, the daily refresh setting and the next run', async () => {
    server.use(
      http.get('/api/accounts/:id/holdings/sync-status', () =>
        HttpResponse.json(buildSyncStatus({ mode: 'daily', nextRunAt: '2026-10-12T10:30:00Z' })),
      ),
    );
    renderPortfolio();

    expect(await screen.findByText('1 new, 1 closed', { exact: false })).toBeInTheDocument();
    expect(screen.getByText(/Auto refresh: daily at 16:00 IST/)).toHaveTextContent('next run 16:00, 12 Oct IST');
  });

  it('says the auto refresh is off', async () => {
    renderPortfolio();

    expect(await screen.findByText('Auto refresh: off')).toBeInTheDocument();
  });

  it('says why an automatic run was skipped', async () => {
    server.use(
      http.get('/api/accounts/:id/holdings/sync-status', () =>
        HttpResponse.json(
          buildSyncStatus({
            mode: 'daily',
            last: {
              ...buildSyncStatus().last!,
              trigger: 'auto',
              status: 'skipped',
              newCount: 0,
              closedCount: 0,
              error: 'The account is not connected to Dhan.',
            },
          }),
        ),
      ),
    );
    renderPortfolio();

    expect(await screen.findByText('Skipped')).toBeInTheDocument();
    expect(screen.getByText(/The account is not connected to Dhan\./)).toBeInTheDocument();
    expect(screen.getByText(/Auto refresh: daily at 16:00 IST/)).toBeInTheDocument();
  });

  it('says "Not synced yet" before the first run, and shows a running sync', async () => {
    server.use(
      http.get('/api/accounts/:id/holdings/sync-status', () => HttpResponse.json(buildSyncStatus({ last: null }))),
    );
    const { unmount } = renderPortfolio();
    expect(await screen.findByText('Not synced yet')).toBeInTheDocument();
    unmount();

    server.use(
      http.get('/api/accounts/:id/holdings/sync-status', () => HttpResponse.json(buildSyncStatus({ running: true }))),
    );
    renderPortfolio();
    expect(await screen.findByText('Sync in progress…')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Syncing…' })).toBeDisabled();
  });
});
