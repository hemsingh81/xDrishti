import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildIncome, buildUnallocatedGroup } from '@/test/dividendFixtures';
import { appliedCss } from '@/test/css';
import { answerGroupHoldings, buildGroup, buildGroups, buildGroupTotal, groupKeys } from '@/test/groupFixtures';
import { stubPhoneWidth } from '@/test/matchMedia';
import { server } from '@/test/server';
import { renderPortfolio } from './testing';

/** Sector groups with income: Energy has dividends, the total has more, and an Unallocated row ends the list. */
function serveIncomeGroups() {
  const requests: URLSearchParams[] = [];
  const holdings: URLSearchParams[] = [];
  server.use(
    http.get('/api/holdings/groups', ({ request }) => {
      requests.push(new URL(request.url).searchParams);
      return HttpResponse.json(
        buildGroups({
          groups: [
            buildGroup({ income: buildIncome() }),
            buildGroup({
              key: groupKeys.it,
              name: 'Information Technology',
              holdings: 2,
              weightPct: 40,
              income: buildIncome({ dividends: 0, dividendsFrom: null, charges: 40, chargesFrom: '2025-03-03' }),
            }),
            buildUnallocatedGroup(),
          ],
          total: buildGroupTotal({
            income: buildIncome({ dividends: 1_400, unallocated: 200, dividendsFrom: '2025-12-05', charges: 215 }),
          }),
        }),
      );
    }),
    http.get('/api/holdings', ({ request }) => {
      const params = new URL(request.url).searchParams;
      holdings.push(params);
      return HttpResponse.json(answerGroupHoldings(params));
    }),
  );
  return { requests, holdings };
}

const groupsTable = () => screen.findByRole('table', { name: 'Holdings by sector' });
const rowOf = (name: string) => screen.getByRole('row', { name: new RegExp(`^${name}`) });

describe('Group by: the Dividends column', () => {
  it('shows the dividends of each group and of the total with the day they are known from, and a dash where none are entered', async () => {
    serveIncomeGroups();
    renderPortfolio('?groupBy=sector');
    const table = await groupsTable();

    expect(within(table).getByRole('columnheader', { name: /Dividends/ })).toBeInTheDocument();
    const energy = within(rowOf('Energy'));
    expect(energy.getByText('₹1,200')).toBeInTheDocument();
    expect(energy.getByText('entered from 5 Dec 2025')).toBeInTheDocument();
    // Only charges recorded for this group: no dividends to show, not a zero.
    expect(within(rowOf('Information Technology')).getAllByRole('cell')[5]).toHaveTextContent(/^—$/);
    expect(within(rowOf('All holdings')).getByText('₹1,400')).toBeInTheDocument();
  });

  it('is sorted by clicking its title, and the order goes to the API as sort=dividends', async () => {
    const requests = serveIncomeGroups();
    const { router } = renderPortfolio('?groupBy=sector');
    await groupsTable();

    await userEvent.click(screen.getByRole('button', { name: 'Sort by Dividends' }));

    await waitFor(() => {
      expect(requests.requests.at(-1)?.get('sort')).toBe('dividends');
    });
    expect(router.state.location.search).toContain('sort=dividends');
    expect(screen.getByRole('columnheader', { name: /Dividends/ })).toHaveAttribute('aria-sort', 'ascending');
  });

  it('is hidden below the md breakpoint, like the invested and day change columns are below lg', async () => {
    serveIncomeGroups();
    renderPortfolio('?groupBy=sector');
    await groupsTable();

    const css = appliedCss(screen.getByRole('columnheader', { name: /Dividends/ }));
    expect(css.responsive).toEqual([
      { query: '(min-width:0px)', css: 'display: none;' },
      { query: '(min-width:900px)', css: 'display: table-cell;' },
    ]);
  });
});

describe('Group by: the Unallocated row', () => {
  it('comes last, says what it is, holds the dividends of no group and has nothing to open', async () => {
    const requests = serveIncomeGroups();
    renderPortfolio('?groupBy=sector');
    await groupsTable();

    const names = within(screen.getByRole('table', { name: 'Holdings by sector' }))
      .getAllByRole('rowheader')
      .map((header) => header.textContent);
    expect(names.at(-1)).toMatch(/^Unallocated/);
    const unallocated = within(rowOf('Unallocated'));
    expect(unallocated.getByText('Dividends and costs that belong to no group · costs ₹25.00')).toBeInTheDocument();
    expect(unallocated.getByText('₹200')).toBeInTheDocument();
    // Not a group of holdings: no button, no expand, and no request for its holdings.
    expect(unallocated.queryByRole('button')).not.toBeInTheDocument();
    await userEvent.click(unallocated.getByText('Unallocated'));
    expect(requests.holdings).toEqual([]);
    expect(screen.queryByRole('grid', { name: 'Unallocated holdings' })).not.toBeInTheDocument();
  });

  it('is not counted as a group: Expand all opens the holdings groups only, and "Collapse all" closes them', async () => {
    const requests = serveIncomeGroups();
    renderPortfolio('?groupBy=sector');
    await groupsTable();

    await userEvent.click(screen.getByRole('button', { name: 'Expand all' }));

    await waitFor(() => {
      expect(requests.holdings.map((p) => p.get('groupKey')).sort()).toEqual([groupKeys.it, groupKeys.energy].sort());
    });
    expect(screen.getByRole('button', { name: 'Expand all' })).toHaveAttribute('aria-disabled', 'true');
  });

  it('is a card of its own on a phone, with the dividends and nothing to open', async () => {
    stubPhoneWidth();
    try {
      serveIncomeGroups();
      renderPortfolio('?groupBy=sector');

      const list = await screen.findByRole('list', { name: 'Holdings by sector' });
      const cards = within(list).getAllByRole('listitem');
      expect(cards).toHaveLength(4);
      const card = within(cards[3]!);
      expect(card.getByText('Unallocated')).toBeInTheDocument();
      expect(card.getByText('Dividends').nextElementSibling).toHaveTextContent('₹200');
      expect(card.queryByRole('button')).not.toBeInTheDocument();
      // The groups with income show their dividends too.
      expect(within(cards[1]!).getByText('Dividends').nextElementSibling).toHaveTextContent('₹1,200');
    } finally {
      vi.unstubAllGlobals();
    }
  });
});
