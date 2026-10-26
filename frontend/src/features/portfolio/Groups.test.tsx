import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { holdingIds, ids } from '@/test/fixtures';
import {
  answerGroupHoldings,
  answerGroups,
  buildGroups,
  buildGroup,
  buildGroupTotal,
  buildUnpricedGroup,
} from '@/test/groupFixtures';
import { problemResponse, server } from '@/test/server';
import { noHoldingsHandlers, renderPortfolio } from './testing';

/** The requests the groups endpoint and the holdings list received, as their query parameters. */
function captureRequests() {
  const groups: URLSearchParams[] = [];
  const holdings: URLSearchParams[] = [];
  const urls: string[] = [];
  server.use(
    http.get('/api/holdings/groups', ({ request }) => {
      const params = new URL(request.url).searchParams;
      groups.push(params);
      return HttpResponse.json(answerGroups(params));
    }),
    http.get('/api/holdings', ({ request }) => {
      const params = new URL(request.url).searchParams;
      holdings.push(params);
      urls.push(request.url);
      return HttpResponse.json(answerGroupHoldings(params));
    }),
  );
  return { groups, holdings, urls };
}

/**
 * Holds the groups back until `release()`: a gate, not a request that never ends (an aborted hanging request can let the next
 * one to the same URL escape the network mock). The test opens it and awaits the answer before it ends.
 */
function holdGroups() {
  let release: () => void = () => undefined;
  const gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  server.use(
    http.get('/api/holdings/groups', async ({ request }) => {
      await gate;
      return HttpResponse.json(answerGroups(new URL(request.url).searchParams));
    }),
  );
  return { release };
}

const groupsTable = (by = 'sector') => screen.findByRole('table', { name: `Holdings by ${by}` });
const rowOf = (name: string | RegExp) =>
  screen.getByRole('row', { name: typeof name === 'string' ? new RegExp(`^${name}`) : name });
const cellTexts = (row: HTMLElement) =>
  within(row)
    .getAllByRole('cell')
    .map((cell) => cell.textContent);
const expander = (name: string) => screen.getByRole('button', { name });
const groupNames = () =>
  within(screen.getByRole('table', { name: /^Holdings by/ }))
    .getAllByRole('rowheader')
    .map((h) => h.textContent.replace(/\d+ in this view$/, ''));

describe('Group by: the control and the URL', () => {
  it('offers None, Account, Bucket, Sector and Basket, with None on: the flat grid, and no groups requested', async () => {
    const requests = captureRequests();
    renderPortfolio();

    const control = await screen.findByRole('group', { name: 'Group by' });
    expect(
      within(control)
        .getAllByRole('button')
        .map((b) => b.textContent),
    ).toEqual(['None', 'Account', 'Bucket', 'Sector', 'Basket']);
    expect(within(control).getByRole('button', { name: 'None' })).toHaveAttribute('aria-pressed', 'true');
    expect(await screen.findByRole('grid', { name: 'Holdings' })).toBeInTheDocument();
    expect(screen.queryByRole('table', { name: /^Holdings by/ })).not.toBeInTheDocument();
    expect(requests.groups).toEqual([]);
  });

  it('puts the grouping in the URL, keeps the other filters and shows the groups; None goes back to the flat list', async () => {
    const requests = captureRequests();
    const { router } = renderPortfolio('?status=all');
    await screen.findByRole('grid', { name: 'Holdings' });

    await userEvent.click(
      within(screen.getByRole('group', { name: 'Group by' })).getByRole('button', { name: 'Sector' }),
    );

    await groupsTable();
    expect(router.state.location.search).toBe('?status=all&groupBy=sector');
    expect(requests.groups[0]?.get('by')).toBe('sector');
    expect(requests.groups[0]?.get('status')).toBe('all');
    expect(screen.queryByRole('grid', { name: 'Holdings' })).not.toBeInTheDocument();

    await userEvent.click(
      within(screen.getByRole('group', { name: 'Group by' })).getByRole('button', { name: 'None' }),
    );
    expect(await screen.findByRole('grid', { name: 'Holdings' })).toBeInTheDocument();
    expect(router.state.location.search).toBe('?status=all');
  });

  it('reads the grouping from a link and marks it pressed; an unknown value falls back to the flat list', async () => {
    renderPortfolio('?groupBy=bucket');
    await groupsTable('bucket');
    expect(
      within(screen.getByRole('group', { name: 'Group by' })).getByRole('button', { name: 'Bucket' }),
    ).toHaveAttribute('aria-pressed', 'true');
  });

  it('falls back to the flat list for a grouping it does not know', async () => {
    renderPortfolio('?groupBy=planet');
    expect(await screen.findByRole('grid', { name: 'Holdings' })).toBeInTheDocument();
  });

  it('is reached with the keyboard like the status buttons', async () => {
    renderPortfolio();
    const sector = within(await screen.findByRole('group', { name: 'Group by' })).getByRole('button', {
      name: 'Sector',
    });
    sector.focus();
    await userEvent.keyboard('{Enter}');
    await groupsTable();
    expect(sector).toHaveAttribute('aria-pressed', 'true');
  });
});

describe('Group by: group rows, the total row and the figures', () => {
  it('shows the total row "All holdings" under the column titles, then one row per group, largest value first', async () => {
    renderPortfolio('?groupBy=sector');
    const table = await groupsTable();

    expect(
      within(table)
        .getAllByRole('columnheader')
        .map((h) => h.textContent),
    ).toEqual(['Sector', 'Holdings', 'Invested', 'Value', 'P&L%', 'XIRR', 'Dividends', 'Day change', 'Weight']);
    expect(groupNames()).toEqual(['All holdings', 'Energy', 'Information Technology', 'Unclassified']);
    expect(cellTexts(rowOf('All holdings'))).toEqual([
      '4',
      '₹1,85,000',
      '₹2,03,400',
      '▲+₹18,400▲+9.95%',
      '▲+11.30%',
      '—',
      '▲+₹900▲+0.56%1 excluded',
      '100.0%',
    ]);
  });

  it('shows invested, value, P&L over percent with sign and arrow, XIRR, day change and the weight of each group', async () => {
    renderPortfolio('?groupBy=sector');
    await groupsTable();

    expect(cellTexts(rowOf('Energy'))).toEqual([
      '1',
      '₹1,00,000',
      '₹1,16,100',
      '▲+₹16,100▲+16.10%',
      '▲+14.20%',
      '—',
      '▲+₹900▲+0.78%',
      '57.1%',
    ]);
    expect(cellTexts(rowOf('Information Technology'))[3]).toBe('▲+₹1,300▲+1.63%');
    expect(cellTexts(rowOf('Information Technology'))[6]).toBe('▼−₹200▼−0.51%1 excluded');
  });

  it('says in words why a group has no XIRR, not only in a tooltip', async () => {
    renderPortfolio('?groupBy=sector');
    await groupsTable();

    const it = rowOf('Information Technology');
    expect(within(it).getByText('Needs 30+ days of history')).toBeInTheDocument();
    expect(within(it).getByRole('img', { name: /first purchase at least 30 days before today/ })).toHaveTextContent(
      '—',
    );
    const unclassified = rowOf('Unclassified');
    expect(within(unclassified).getByText('Not meaningful (beyond ±1,000 %)')).toBeInTheDocument();
    expect(within(unclassified).getByRole('img', { name: /beyond ±1,000 %/ })).toHaveTextContent('n/m');
  });

  it('writes the weight as a number the bar only illustrates: the bar is hidden from assistive technology', async () => {
    renderPortfolio('?groupBy=sector');
    await groupsTable();

    const row = rowOf('Energy');
    expect(within(row).getByText('57.1%')).toBeInTheDocument();
    expect(within(row).queryByRole('progressbar')).not.toBeInTheDocument();
    expect(within(row).queryByRole('meter')).not.toBeInTheDocument();
    const percent = within(row).getByText('57.1%');
    expect(percent.previousElementSibling).toHaveAttribute('aria-hidden', 'true');
  });

  it('flags groups whose holdings have no price, and says so in full when none of them has one', async () => {
    server.use(
      http.get('/api/holdings/groups', () =>
        HttpResponse.json(buildGroups({ groups: [buildGroup({ holdings: 3, unpriced: 1 }), buildUnpricedGroup()] })),
      ),
    );
    renderPortfolio('?groupBy=sector');
    await groupsTable();

    expect(within(rowOf('Energy')).getByText('1 without a price')).toBeInTheDocument();
    const oil = rowOf('Oil & Gas');
    expect(within(oil).getByText('2 holdings without a price')).toBeInTheDocument();
    // No invested (the API sends 0: the unpriced are left out), value, P&L, XIRR or weight: dashes, never zeros or a loss of 100 %.
    expect(cellTexts(oil)).toEqual([
      '22 holdings without a price',
      '—',
      '—',
      '—',
      '—No price yet',
      '—',
      '—2 excluded',
      '—',
    ]);
    expect(cellTexts(oil).join(' ')).not.toMatch(/₹0|100\.0%|−₹/);
    expect(within(oil).getByRole('img', { name: /None of these holdings has a price yet/ })).toHaveTextContent('—');
    expect(
      within(oil).getAllByRole('img', { name: /No price yet, so there is no current value/ }).length,
    ).toBeGreaterThan(0);
  });

  it('keeps the invested amount of a group that is only partly unpriced: its priced part is real money', async () => {
    server.use(
      http.get('/api/holdings/groups', () =>
        HttpResponse.json(buildGroups({ groups: [buildGroup({ holdings: 3, unpriced: 1 })] })),
      ),
    );
    renderPortfolio('?groupBy=sector');
    await groupsTable();
    expect(cellTexts(rowOf('Energy'))[1]).toBe('₹1,00,000');
  });

  it('shows a dash for the invested amount of the total row when nothing in the scope has a price', async () => {
    server.use(
      http.get('/api/holdings/groups', () =>
        HttpResponse.json(
          buildGroups({
            groups: [buildUnpricedGroup()],
            total: buildGroupTotal({
              holdings: 2,
              unpriced: 2,
              invested: 0,
              value: null,
              pnl: null,
              pnlPct: null,
              xirr: null,
              xirrState: 'no_price',
              dayChange: null,
              dayChangePct: null,
              weightPct: null,
            }),
          }),
        ),
      ),
    );
    renderPortfolio('?groupBy=sector');
    await groupsTable();

    expect(cellTexts(rowOf('All holdings'))[1]).toBe('—');
    expect(cellTexts(rowOf('All holdings')).join(' ')).not.toContain('₹0');
  });

  it('says in the day change how many holdings it leaves out', async () => {
    renderPortfolio('?groupBy=sector');
    await groupsTable();

    expect(within(rowOf('Information Technology')).getByText('1 excluded')).toHaveAttribute(
      'title',
      '1 holding has no previous close yet and is left out of the day change.',
    );
    expect(within(rowOf('Energy')).queryByText(/excluded/)).not.toBeInTheDocument();
    expect(within(rowOf('All holdings')).getByText('1 excluded')).toBeInTheDocument();
  });
});

describe('Group by: the order of the groups', () => {
  it('sends the order explicitly (-value) and marks the Value column descending', async () => {
    const requests = captureRequests();
    renderPortfolio('?groupBy=sector');
    await groupsTable();

    expect(requests.groups[0]?.get('sort')).toBe('-value');
    expect(screen.getByRole('columnheader', { name: 'Value' })).toHaveAttribute('aria-sort', 'descending');
  });

  it('sorts through the API when a column title is pressed, both ways, and keeps it in the URL', async () => {
    const requests = captureRequests();
    const { router } = renderPortfolio('?groupBy=sector');
    await groupsTable();

    await userEvent.click(screen.getByRole('button', { name: 'Sort by Invested' }));

    await waitFor(() => {
      expect(router.state.location.search).toBe('?groupBy=sector&sort=invested');
    });
    expect(requests.groups.at(-1)?.get('sort')).toBe('invested');
    await waitFor(() => {
      expect(groupNames()).toEqual(['All holdings', 'Unclassified', 'Information Technology', 'Energy']);
    });
    expect(screen.getByRole('columnheader', { name: 'Invested' })).toHaveAttribute('aria-sort', 'ascending');

    await userEvent.click(screen.getByRole('button', { name: 'Sort by Invested' }));
    await waitFor(() => {
      expect(requests.groups.at(-1)?.get('sort')).toBe('-invested');
    });
  });

  it('sorts by the percentage and by the name through their own buttons', async () => {
    const requests = captureRequests();
    renderPortfolio('?groupBy=sector');
    await groupsTable();

    await userEvent.click(screen.getByRole('button', { name: 'Sort by P&L %' }));
    await waitFor(() => {
      expect(requests.groups.at(-1)?.get('sort')).toBe('pnlPct');
    });
    await userEvent.click(screen.getByRole('button', { name: 'Sort by Sector' }));
    await waitFor(() => {
      expect(requests.groups.at(-1)?.get('sort')).toBe('name');
    });
  });

  it('puts the groups without a value last whichever way the order goes', async () => {
    server.use(
      http.get('/api/holdings/groups', ({ request }) => {
        const data = answerGroups(new URL(request.url).searchParams);
        return HttpResponse.json({
          ...data,
          groups: [...data.groups, buildGroup({ key: 'x', name: 'Unpriced', value: null, pnl: null, pnlPct: null })],
        });
      }),
    );
    renderPortfolio('?groupBy=sector');
    await groupsTable();

    expect(groupNames().at(-1)).toBe('Unpriced');
  });

  it('follows an order from the link when the groups have it, and uses -value when they do not', async () => {
    const requests = captureRequests();
    const { unmount } = renderPortfolio('?groupBy=sector&sort=-pnl');
    await groupsTable();
    expect(requests.groups[0]?.get('sort')).toBe('-pnl');
    unmount();

    renderPortfolio('?groupBy=sector&sort=-qty');
    await groupsTable();
    expect(requests.groups.at(-1)?.get('sort')).toBe('-value');
  });
});

describe('Group by: opening a group', () => {
  it('opens a group to its holdings with the holdings columns, asking the list for that group only', async () => {
    const requests = captureRequests();
    renderPortfolio('?groupBy=sector');
    await groupsTable();
    expect(expander('Energy')).toHaveAttribute('aria-expanded', 'false');
    expect(requests.holdings).toEqual([]);

    await userEvent.click(expander('Energy'));

    const grid = await screen.findByRole('grid', { name: 'Energy holdings' });
    expect(expander('Energy')).toHaveAttribute('aria-expanded', 'true');
    expect(expander('Energy')).toHaveAttribute('aria-controls');
    expect(await within(grid).findByRole('gridcell', { name: 'RELIANCE' })).toBeInTheDocument();
    expect(
      within(grid)
        .getAllByRole('columnheader')
        .map((h) => h.textContent),
    ).toContain('Avg cost');
    expect(Object.fromEntries(requests.holdings[0] ?? [])).toMatchObject({
      groupBy: 'sector',
      groupKey: 'Energy',
      page: '1',
      pageSize: '50',
      sort: 'symbol',
    });
  });

  it('opens with Enter and closes with Space, keeping the opened holdings in the cache for the next time', async () => {
    const requests = captureRequests();
    renderPortfolio('?groupBy=sector');
    await groupsTable();

    expander('Energy').focus();
    await userEvent.keyboard('{Enter}');
    await screen.findByRole('gridcell', { name: 'RELIANCE' });

    await userEvent.keyboard(' ');
    expect(screen.queryByRole('grid', { name: 'Energy holdings' })).not.toBeInTheDocument();
    expect(expander('Energy')).toHaveAttribute('aria-expanded', 'false');

    await userEvent.click(expander('Energy'));
    // The cached rows are there at once, with no loading state in between.
    expect(screen.getByRole('gridcell', { name: 'RELIANCE' })).toBeInTheDocument();
    expect(requests.holdings.every((p) => p.get('groupKey') === 'Energy')).toBe(true);
  });

  it('asks for the group "Unclassified" by the reserved key ~none', async () => {
    const requests = captureRequests();
    renderPortfolio('?groupBy=sector');
    await groupsTable();

    await userEvent.click(expander('Unclassified'));

    expect(await screen.findByRole('gridcell', { name: /^SMALLCO/ })).toBeInTheDocument();
    expect(requests.holdings[0]?.get('groupKey')).toBe('~none');
  });

  it('URL-encodes a group key with spaces and an ampersand', async () => {
    const requests = captureRequests();
    server.use(
      http.get('/api/holdings/groups', () =>
        HttpResponse.json(buildGroups({ groups: [buildGroup({ key: 'Oil & Gas', name: 'Oil & Gas' })] })),
      ),
    );
    renderPortfolio('?groupBy=sector');
    await groupsTable();

    await userEvent.click(expander('Oil & Gas'));

    await waitFor(() => {
      expect(requests.holdings).toHaveLength(1);
    });
    expect(requests.holdings[0]?.get('groupKey')).toBe('Oil & Gas');
    expect(requests.urls[0]).not.toMatch(/groupKey=Oil[ +]&/);
    expect(requests.urls[0]).toMatch(/groupKey=Oil(%20|\+)%26(%20|\+)Gas/);
  });

  it('gives an opened group the scope: account, bucket, status, search and sort', async () => {
    const requests = captureRequests();
    renderPortfolio(`?groupBy=sector&account=${ids.main}&status=all&q=re&sort=-pnl`);
    await groupsTable();

    await userEvent.click(expander('Energy'));
    await screen.findByRole('grid', { name: 'Energy holdings' });

    expect(Object.fromEntries(requests.holdings[0] ?? [])).toMatchObject({
      accountId: ids.main,
      status: 'all',
      q: 're',
      sort: '-pnl',
      groupBy: 'sector',
      groupKey: 'Energy',
    });
    expect(requests.groups[0]?.get('accountId')).toBe(ids.main);
    expect(requests.groups[0]?.get('q')).toBe('re');
  });

  it('opens the holding drawer from an opened group, with the row in it', async () => {
    const { router } = renderPortfolio('?groupBy=sector');
    await groupsTable();
    await userEvent.click(expander('Energy'));

    await userEvent.click(await screen.findByRole('gridcell', { name: 'RELIANCE' }));

    expect(router.state.location.search).toContain(`holding=${holdingIds.reliance}`);
    const drawer = await screen.findByRole('dialog', { name: 'Holding details' });
    expect(await within(drawer).findByText('Reliance Industries')).toBeInTheDocument();
  });

  it('lists a holding that is in two baskets under both', async () => {
    renderPortfolio('?groupBy=basket');
    await groupsTable('basket');

    await userEvent.click(expander('Nifty 50 core'));
    await userEvent.click(expander('IT leaders'));

    const nifty = await screen.findByRole('grid', { name: 'Nifty 50 core holdings' });
    const leaders = await screen.findByRole('grid', { name: 'IT leaders holdings' });
    expect(await within(nifty).findByRole('gridcell', { name: 'TCS' })).toBeInTheDocument();
    expect(await within(leaders).findByRole('gridcell', { name: 'TCS' })).toBeInTheDocument();
  });

  it('shows the account column in an opened group when several accounts are in view', async () => {
    const { twoPortfolioAccounts } = await import('./testing');
    server.use(twoPortfolioAccounts());
    renderPortfolio('?groupBy=sector');
    await groupsTable();

    await userEvent.click(expander('Energy'));

    const grid = await screen.findByRole('grid', { name: 'Energy holdings' });
    expect(within(grid).getByRole('columnheader', { name: 'Account' })).toBeInTheDocument();
  });

  it('says so when the holdings of a group cannot be loaded, and retries just that group', async () => {
    let fail = true;
    server.use(
      http.get('/api/holdings', ({ request }) =>
        fail
          ? new HttpResponse(null, { status: 500 })
          : HttpResponse.json(answerGroupHoldings(new URL(request.url).searchParams)),
      ),
    );
    renderPortfolio('?groupBy=sector');
    await groupsTable();

    await userEvent.click(expander('Energy'));
    expect(await screen.findByText('The holdings of this group could not be loaded.')).toBeInTheDocument();
    expect(screen.queryByRole('grid', { name: 'Energy holdings' })).not.toBeInTheDocument();

    fail = false;
    const alert = screen.getByText('The holdings of this group could not be loaded.').closest('[role="alert"]');
    await userEvent.click(within(alert as HTMLElement).getByRole('button', { name: 'Retry' }));
    expect(await screen.findByRole('gridcell', { name: 'RELIANCE' })).toBeInTheDocument();
  });
});

describe('Group by: Expand all and Collapse all', () => {
  it('opens and closes every group when there are 20 or fewer', async () => {
    const requests = captureRequests();
    renderPortfolio('?groupBy=sector');
    await groupsTable();
    expect(screen.getByRole('button', { name: 'Collapse all' })).toHaveAttribute('aria-disabled', 'true');

    await userEvent.click(screen.getByRole('button', { name: 'Expand all' }));

    expect(await screen.findByRole('grid', { name: 'Energy holdings' })).toBeInTheDocument();
    expect(await screen.findByRole('grid', { name: 'Information Technology holdings' })).toBeInTheDocument();
    expect(await screen.findByRole('grid', { name: 'Unclassified holdings' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Expand all' })).toHaveAttribute('aria-disabled', 'true');
    await waitFor(() => {
      expect(requests.holdings.map((p) => p.get('groupKey')).sort()).toEqual([
        'Energy',
        'Information Technology',
        '~none',
      ]);
    });

    await userEvent.click(screen.getByRole('button', { name: 'Collapse all' }));
    expect(screen.queryByRole('grid', { name: /holdings$/ })).not.toBeInTheDocument();
    expect(expander('Energy')).toHaveAttribute('aria-expanded', 'false');
  });

  it('does not count open groups that a new filter has removed: Expand all stays available', async () => {
    server.use(
      http.get('/api/holdings/groups', ({ request }) => {
        const params = new URL(request.url).searchParams;
        const data = answerGroups(params);
        return HttpResponse.json(
          params.get('q') ? { ...data, groups: data.groups.filter((g) => g.name === 'Information Technology') } : data,
        );
      }),
    );
    renderPortfolio('?groupBy=sector');
    await groupsTable();
    await userEvent.click(expander('Energy'));
    await screen.findByRole('grid', { name: 'Energy holdings' });

    await userEvent.type(screen.getByRole('searchbox', { name: 'Search holdings' }), 'tcs');

    await waitFor(() => {
      expect(groupNames()).toEqual(['All holdings', 'Information Technology']);
    });
    // One group is listed and none of the listed ones is open (Energy is gone).
    expect(screen.getByRole('button', { name: 'Expand all' })).toHaveAttribute('aria-disabled', 'false');
    expect(screen.getByRole('button', { name: 'Collapse all' })).toHaveAttribute('aria-disabled', 'true');
    await userEvent.click(screen.getByRole('button', { name: 'Expand all' }));
    expect(await screen.findByRole('grid', { name: 'Information Technology holdings' })).toBeInTheDocument();
  });

  it('is not offered for more than 20 groups', async () => {
    server.use(
      http.get('/api/holdings/groups', () =>
        HttpResponse.json(
          buildGroups({
            groups: Array.from({ length: 21 }, (_, i) =>
              buildGroup({ key: `Sector ${String(i)}`, name: `Sector ${String(i)}` }),
            ),
          }),
        ),
      ),
    );
    renderPortfolio('?groupBy=sector');
    await groupsTable();

    expect(screen.queryByRole('button', { name: 'Expand all' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Collapse all' })).not.toBeInTheDocument();
    expect(expander('Sector 0')).toBeInTheDocument();
  });

  it('forgets what was open when the grouping changes', async () => {
    renderPortfolio('?groupBy=sector');
    await groupsTable();
    await userEvent.click(expander('Energy'));
    await screen.findByRole('grid', { name: 'Energy holdings' });

    await userEvent.click(
      within(screen.getByRole('group', { name: 'Group by' })).getByRole('button', { name: 'Account' }),
    );
    await groupsTable('account');
    expect(screen.queryByRole('grid', { name: /holdings$/ })).not.toBeInTheDocument();
    expect(expander('Test Dhan Main')).toHaveAttribute('aria-expanded', 'false');
  });
});

describe('Group by: baskets overlap', () => {
  const note = 'Baskets overlap — totals do not add up to the portfolio';

  it('says that the totals of baskets do not add up, and still shows the scope total above the baskets', async () => {
    renderPortfolio('?groupBy=basket');
    const table = await groupsTable('basket');

    expect(screen.getByText(note)).toBeInTheDocument();
    expect(groupNames()).toEqual(['All holdings', 'Nifty 50 core', 'IT leaders', 'No basket']);
    expect(cellTexts(within(table).getByRole('row', { name: /^All holdings/ }))[2]).toBe('₹2,03,400');
    // The weights are shares of the scope's value, so they add up to more than 100 %.
    expect(within(rowOf('Nifty 50 core')).getByText('97.1%')).toBeInTheDocument();
    expect(within(rowOf('IT leaders')).getByText('40.0%')).toBeInTheDocument();
  });

  it.each(['account', 'bucket', 'sector'])('does not say it for %s, which adds up', async (by) => {
    renderPortfolio(`?groupBy=${by}`);
    await groupsTable(by);
    expect(screen.queryByText(note)).not.toBeInTheDocument();
  });
});

describe('Group by: loading, errors and empty views', () => {
  it('shows a loading indicator while the groups load', async () => {
    const gate = holdGroups();
    renderPortfolio('?groupBy=sector');

    expect(await screen.findByText('Loading groups')).toBeInTheDocument();

    gate.release();
    expect(await groupsTable()).toBeInTheDocument();
  });

  it('shows an error with Retry when the groups fail, and the groups once it works', async () => {
    let fail = true;
    server.use(
      http.get('/api/holdings/groups', ({ request }) =>
        fail
          ? new HttpResponse(null, { status: 500 })
          : HttpResponse.json(answerGroups(new URL(request.url).searchParams)),
      ),
    );
    renderPortfolio('?groupBy=sector');

    const alert = await screen.findByText('The groups could not be loaded.');
    fail = false;
    await userEvent.click(within(alert.closest('[role="alert"]')!).getByRole('button', { name: 'Retry' }));
    expect(await groupsTable()).toBeInTheDocument();
  });

  it('shows the words of the server for a refused request, and a way back to the flat list instead of Retry', async () => {
    server.use(
      http.get('/api/holdings/groups', () =>
        problemResponse({ title: 'The grouping is not valid.', code: 'portfolio.invalid', status: 400 }, 400),
      ),
    );
    const { router } = renderPortfolio('?groupBy=sector');

    const message = await screen.findByText('The grouping is not valid.');
    const alert = message.closest<HTMLElement>('[role="alert"]')!;
    expect(within(alert).queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument();
    await userEvent.click(within(alert).getByRole('button', { name: 'Group by None' }));

    expect(await screen.findByRole('grid', { name: 'Holdings' })).toBeInTheDocument();
    expect(router.state.location.search).toBe('');
  });

  it('says "No holdings in this scope" when the filters leave nothing, and clearing them keeps the grouping', async () => {
    server.use(
      http.get('/api/holdings/groups', ({ request }) => {
        const params = new URL(request.url).searchParams;
        return HttpResponse.json(
          params.get('q') ? buildGroups({ groups: [], total: buildGroups().total }) : answerGroups(params),
        );
      }),
    );
    const { router } = renderPortfolio('?groupBy=sector&q=zzz');

    expect(await screen.findByText('No holdings in this scope')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Clear filters' }));

    expect(await groupsTable()).toBeInTheDocument();
    expect(router.state.location.search).toBe('?groupBy=sector');
  });

  it('points to the Portfolio settings when there is no holding at all', async () => {
    server.use(
      ...noHoldingsHandlers(),
      http.get('/api/holdings/groups', () => HttpResponse.json(buildGroups({ groups: [] }))),
    );
    renderPortfolio('?groupBy=sector');

    expect(await screen.findByText('No holdings yet')).toBeInTheDocument();
  });

  it('keeps the groups and says they may be out of date when a refresh fails', async () => {
    let calls = 0;
    server.use(
      http.get('/api/holdings/groups', ({ request }) => {
        calls += 1;
        return calls === 1
          ? HttpResponse.json(answerGroups(new URL(request.url).searchParams))
          : new HttpResponse(null, { status: 500 });
      }),
    );
    const { client } = renderPortfolio('?groupBy=sector');
    await groupsTable();

    await client.refetchQueries({ queryKey: ['portfolio', 'groups', 'list'] });

    expect(await screen.findByText(/may be out of date/)).toBeInTheDocument();
    expect(rowOf('Energy')).toBeInTheDocument();
  });

  it('keeps the groups on screen, dimmed, while a new order loads', async () => {
    renderPortfolio('?groupBy=sector');
    await groupsTable();
    const gate = holdGroups();

    await userEvent.click(screen.getByRole('button', { name: 'Sort by Invested' }));

    // The old order stays (dimmed, marked busy) until the new one arrives.
    expect(groupNames()).toEqual(['All holdings', 'Energy', 'Information Technology', 'Unclassified']);
    await waitFor(() => {
      expect(rowOf('Energy').closest('[aria-busy="true"]')).not.toBeNull();
    });

    gate.release();
    await waitFor(() => {
      expect(groupNames()).toEqual(['All holdings', 'Unclassified', 'Information Technology', 'Energy']);
    });
  });
});

describe('Group by: re-reading', () => {
  it('reads the groups again when a priced holding changes through Sync now, opened groups too', async () => {
    const requests = captureRequests();
    server.use(
      http.post('/api/accounts/:id/holdings/sync', async () => {
        const { buildSyncRun } = await import('@/test/fixtures');
        return HttpResponse.json(buildSyncRun());
      }),
    );
    renderPortfolio('?groupBy=sector');
    await groupsTable();
    await userEvent.click(expander('Energy'));
    await screen.findByRole('gridcell', { name: 'RELIANCE' });
    const before = { groups: requests.groups.length, holdings: requests.holdings.length };

    const button = await screen.findByRole('button', { name: 'Sync now' });
    await waitFor(() => {
      expect(button).toBeEnabled();
    });
    await userEvent.click(button);

    await waitFor(() => {
      expect(requests.groups.length).toBeGreaterThan(before.groups);
      expect(requests.holdings.length).toBeGreaterThan(before.holdings);
    });
  });

  it('marks the holdings of a group that is closed as stale, so they are read again when it is opened', async () => {
    const requests = captureRequests();
    const { client } = renderPortfolio('?groupBy=sector');
    await groupsTable();
    await userEvent.click(expander('Energy'));
    await screen.findByRole('gridcell', { name: 'RELIANCE' });
    await userEvent.click(expander('Energy'));
    const before = requests.holdings.length;

    await client.invalidateQueries({ queryKey: ['portfolio', 'groups'] });
    expect(requests.holdings).toHaveLength(before);

    await userEvent.click(expander('Energy'));
    await waitFor(() => {
      expect(requests.holdings.length).toBeGreaterThan(before);
    });
  });
});

describe('Group by: a phone', () => {
  beforeEach(async () => {
    const { stubPhoneWidth } = await import('@/test/matchMedia');
    stubPhoneWidth();
  });
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('shows a card per group, after the total card, with labelled figures', async () => {
    renderPortfolio('?groupBy=sector');

    const list = await screen.findByRole('list', { name: 'Holdings by sector' });
    const cards = within(list).getAllByRole('listitem');
    expect(cards).toHaveLength(4);
    expect(within(cards[0]!).getByText('All holdings')).toBeInTheDocument();
    const energy = cards[1]!;
    expect(within(energy).getByText('Value').nextElementSibling).toHaveTextContent('₹1,16,100');
    expect(within(energy).getByText('P&L').nextElementSibling).toHaveTextContent('▲+₹16,100▲+16.10%');
    expect(within(energy).getByText('XIRR').nextElementSibling).toHaveTextContent('▲+14.20%');
    expect(within(energy).getByText('Weight').nextElementSibling).toHaveTextContent('57.1%');
    expect(within(energy).getByText('Invested').nextElementSibling).toHaveTextContent('₹1,00,000');
    expect(within(cards[2]!).getByText('1 excluded')).toBeInTheDocument();
    expect(screen.queryByRole('table', { name: /^Holdings by/ })).not.toBeInTheDocument();
  });

  it('shows a dash, not ₹0, for the invested amount of a card whose holdings have no price', async () => {
    server.use(
      http.get('/api/holdings/groups', () => HttpResponse.json(buildGroups({ groups: [buildUnpricedGroup()] }))),
    );
    renderPortfolio('?groupBy=sector');

    const list = await screen.findByRole('list', { name: 'Holdings by sector' });
    const card = within(list).getAllByRole('listitem')[1]!;
    expect(within(card).getByText('Invested').nextElementSibling).toHaveTextContent('—');
    expect(within(card).getByText('2 holdings without a price')).toBeInTheDocument();
  });

  it('opens a card to its holdings', async () => {
    renderPortfolio('?groupBy=sector');
    await screen.findByRole('list', { name: 'Holdings by sector' });

    await userEvent.click(expander('Energy'));

    expect(await screen.findByRole('gridcell', { name: 'RELIANCE' })).toBeInTheDocument();
    expect(expander('Energy')).toHaveAttribute('aria-expanded', 'true');
  });

  it('puts the order in a small control instead of column titles', async () => {
    const requests = captureRequests();
    const { router } = renderPortfolio('?groupBy=sector');
    await screen.findByRole('list', { name: 'Holdings by sector' });
    const control = screen.getByRole('combobox', { name: 'Sort groups by' });
    expect(control).toHaveValue('-value');

    await userEvent.selectOptions(control, 'pnl');

    await waitFor(() => {
      expect(requests.groups.at(-1)?.get('sort')).toBe('pnl');
    });
    expect(router.state.location.search).toBe('?groupBy=sector&sort=pnl');
  });

  it('shows the note for baskets and the flag for holdings without a price', async () => {
    server.use(
      http.get('/api/holdings/groups', () =>
        HttpResponse.json(
          buildGroups({
            by: 'basket',
            additive: false,
            groups: [buildGroup({ holdings: 2, unpriced: 1 })],
          }),
        ),
      ),
    );
    renderPortfolio('?groupBy=basket');

    expect(await screen.findByText('Baskets overlap — totals do not add up to the portfolio')).toBeInTheDocument();
    expect(await screen.findByText('1 without a price')).toBeInTheDocument();
  });
});
