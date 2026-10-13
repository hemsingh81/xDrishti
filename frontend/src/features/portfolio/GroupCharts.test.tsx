import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { categoryStub, drawnCategorySeries } from '@/test/chartStub';
import { answerGroups, buildGroup, buildGroups } from '@/test/groupFixtures';
import { scrollToEnd } from '@/test/intersection';
import { server } from '@/test/server';
import { categorical } from '@/theme';
import { renderPortfolio } from './testing';

const storageKey = 'xd.portfolio.charts.collapsed';

// A tall window: the card opens expanded (a short one starts it collapsed, tested below).
const windowHeight = window.innerHeight;
beforeEach(() => {
  window.sessionStorage.clear();
  window.innerHeight = 1200;
});
afterEach(() => {
  window.innerHeight = windowHeight;
});

function captureGroups() {
  const requests: URLSearchParams[] = [];
  server.use(
    http.get('/api/holdings/groups', ({ request }) => {
      const params = new URL(request.url).searchParams;
      requests.push(params);
      return HttpResponse.json(answerGroups(params));
    }),
  );
  return requests;
}

/** The Charts card, once it has loaded the groups and is near the viewport (the charts load lazily). */
async function chartsCard() {
  const card = await screen.findByRole('region', { name: 'Charts' });
  scrollToEnd();
  await within(card).findAllByTestId('category-canvas');
  return card;
}

describe('Group by: the Charts card', () => {
  it('has no charts for the flat list', async () => {
    renderPortfolio();
    await screen.findByRole('grid', { name: 'Holdings' });
    expect(screen.queryByRole('region', { name: 'Charts' })).not.toBeInTheDocument();
  });

  it('shows the allocation donut and the sector exposure by account for Sector', async () => {
    renderPortfolio('?groupBy=sector');
    const card = await chartsCard();

    expect(within(card).getByRole('heading', { name: 'Allocation by sector' })).toBeInTheDocument();
    expect(within(card).getByRole('heading', { name: 'Sector exposure by account' })).toBeInTheDocument();
    const donut = within(card).getByRole('img', { name: /^Allocation by sector, 3 slices, total ₹2,03,400/ });
    expect(donut).toHaveAccessibleName(
      expect.stringContaining(
        'Energy 57.1% (₹1,16,100); Information Technology 40.0% (₹81,300); Unclassified 2.9% (₹6,000)',
      ),
    );
    const bars = within(card).getByRole('img', { name: /^Sector exposure by account, 3 bars/ });
    expect(bars).toHaveAccessibleName(
      expect.stringContaining('Information Technology ₹81,300 (Test Dhan Main ₹38,800, Test Dhan Swing ₹42,500)'),
    );
  });

  it('shows the sectors of each account for Account', async () => {
    renderPortfolio('?groupBy=account');
    const card = await chartsCard();

    expect(within(card).getByRole('heading', { name: 'Allocation by account' })).toBeInTheDocument();
    const bars = within(card).getByRole('img', { name: /^Sector mix of each account, 2 bars/ });
    expect(bars).toHaveAccessibleName(
      expect.stringContaining('Test Dhan Main ₹1,54,900 (Energy ₹1,16,100, Information Technology ₹38,800)'),
    );
  });

  it.each(['bucket', 'basket'])('shows only the donut for %s: the stacked bar is hidden', async (by) => {
    const requests = captureGroups();
    renderPortfolio(`?groupBy=${by}`);
    const card = await chartsCard();

    expect(within(card).getAllByRole('img')).toHaveLength(1);
    expect(within(card).getByRole('heading', { name: `Allocation by ${by}` })).toBeInTheDocument();
    expect(within(card).queryByRole('heading', { name: /exposure|mix of/ })).not.toBeInTheDocument();
    expect(requests[0]?.has('stackBy')).toBe(false);
  });

  it('says that basket shares are of the combined value of overlapping baskets', async () => {
    renderPortfolio('?groupBy=basket');
    const card = await chartsCard();
    expect(within(card).getByText(/Baskets overlap, so a holding counts in each of its baskets/)).toBeInTheDocument();
  });

  it.each([
    ['sector', 'account'],
    ['account', 'sector'],
  ])(
    'asks for the other dimension when grouping by %s (stackBy=%s), in the one request the table uses',
    async (by, stackBy) => {
      const requests = captureGroups();
      renderPortfolio(`?groupBy=${by}`);
      await chartsCard();

      expect(requests).toHaveLength(1);
      expect(requests[0]?.get('stackBy')).toBe(stackBy);
      expect(requests[0]?.get('by')).toBe(by);
    },
  );

  it('colours the slices and the stacks from the categorical palette, in order of size', async () => {
    renderPortfolio('?groupBy=sector');
    await chartsCard();

    const pie = categoryStub.calls
      .map((c) => c.option.series as { type: string; data: { itemStyle: { color: string } }[] }[])
      .find((series) => series[0]?.type === 'pie');
    expect(pie?.[0]?.data.map((d) => d.itemStyle.color)).toEqual([
      categorical.light[0],
      categorical.light[1],
      categorical.light[2],
    ]);
    const bar = drawnCategorySeries().filter((s) => s.type === 'bar');
    expect(bar.map((s) => [s.name, s.itemStyle?.color])).toEqual([
      ['Test Dhan Main', categorical.light[0]],
      ['Test Dhan Swing', categorical.light[1]],
    ]);
  });

  it('merges the groups beyond the eight largest into "Other"', async () => {
    server.use(
      http.get('/api/holdings/groups', () =>
        HttpResponse.json(
          buildGroups({
            groups: Array.from({ length: 10 }, (_, i) =>
              buildGroup({ key: `S${String(i)}`, name: `Sector ${String(i)}`, value: 1_000 - i * 10 }),
            ),
          }),
        ),
      ),
    );
    renderPortfolio('?groupBy=sector');
    const card = await chartsCard();

    const legend = within(card).getAllByRole('group', { name: 'Slices shown' })[0]!;
    const names = within(legend)
      .getAllByRole('button')
      .map((b) => b.textContent);
    expect(names).toHaveLength(9);
    expect(names.at(-1)).toMatch(/^Other/);
    expect(names[0]).toMatch(/^Sector 0/);
  });

  it('leaves out a group that has no price, and says there is nothing to chart when none has', async () => {
    server.use(
      http.get('/api/holdings/groups', () =>
        HttpResponse.json(
          buildGroups({ groups: [buildGroup({ value: null, pnl: null, pnlPct: null, weightPct: null, unpriced: 1 })] }),
        ),
      ),
    );
    renderPortfolio('?groupBy=sector');

    expect(await screen.findByText('Nothing to chart yet')).toBeInTheDocument();
    expect(screen.queryByTestId('category-canvas')).not.toBeInTheDocument();
  });

  it('hides a slice from the donut with its legend button and shows the table alternative', async () => {
    renderPortfolio('?groupBy=sector');
    const card = await chartsCard();

    const legend = within(card).getAllByRole('group', { name: 'Slices shown' })[0]!;
    await userEvent.click(within(legend).getByRole('button', { name: /^Energy/ }));
    expect(within(card).getByRole('img', { name: /^Allocation by sector, 2 slices/ })).toBeInTheDocument();

    await userEvent.click(within(card).getAllByRole('button', { name: 'Show as table' })[0]!);
    const table = within(card).getByRole('table', { name: 'Allocation by sector, data table' });
    expect(within(table).getByRole('rowheader', { name: 'Information Technology' })).toBeInTheDocument();
    expect(within(table).queryByRole('rowheader', { name: 'Energy' })).not.toBeInTheDocument();
  });

  it('loads the charts only when the card nears the viewport, with a skeleton of the chart height until then', async () => {
    renderPortfolio('?groupBy=sector');
    const card = await screen.findByRole('region', { name: 'Charts' });

    expect((await within(card).findAllByRole('status', { name: 'Loading chart' })).length).toBeGreaterThan(0);
    expect(within(card).queryByTestId('category-canvas')).not.toBeInTheDocument();
    expect(categoryStub.calls).toEqual([]);

    scrollToEnd();
    expect((await within(card).findAllByTestId('category-canvas')).length).toBe(2);
  });

  it('shows a skeleton of the chart height while the groups load', async () => {
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
    renderPortfolio('?groupBy=sector');
    const card = await screen.findByRole('region', { name: 'Charts' });
    expect(within(card).getAllByRole('status', { name: 'Loading chart' }).length).toBeGreaterThan(0);

    release();
    expect(await screen.findByRole('table', { name: 'Holdings by sector' })).toBeInTheDocument();
  });

  it('says the charts wait for the groups when they cannot be loaded', async () => {
    server.use(http.get('/api/holdings/groups', () => new HttpResponse(null, { status: 500 })));
    renderPortfolio('?groupBy=sector');

    expect(await screen.findByText('The charts need the groups below, which could not be loaded.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });

  it('shows a chart that fails to load as a warning with the table, and Try again', async () => {
    categoryStub.fail = true;
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    renderPortfolio('?groupBy=sector');
    const card = await screen.findByRole('region', { name: 'Charts' });
    scrollToEnd();

    expect((await within(card).findAllByText(/The chart could not be loaded/)).length).toBe(2);
    expect(within(card).getAllByRole('button', { name: 'Try again' })).toHaveLength(2);
    expect(within(card).getAllByRole('table')).toHaveLength(2);
  });

  it('follows the filters: a new search asks for the groups again and redraws', async () => {
    const requests = captureGroups();
    renderPortfolio('?groupBy=sector');
    await chartsCard();

    await userEvent.type(screen.getByRole('searchbox', { name: 'Search holdings' }), 'tcs');

    await waitFor(() => {
      expect(requests.at(-1)?.get('q')).toBe('tcs');
    });
  });
});

describe('Group by: the Charts card on a short or a tall window', () => {
  it('starts collapsed on a window shorter than 1000 px, so the groups stay in sight, and opens on request', async () => {
    window.innerHeight = 900;
    renderPortfolio('?groupBy=sector');

    const show = await screen.findByRole('button', { name: 'Show charts' });
    await screen.findByRole('table', { name: 'Holdings by sector' });
    expect(categoryStub.calls).toEqual([]);
    expect(window.sessionStorage.getItem(storageKey)).toBeNull();

    await userEvent.click(show);
    expect(await screen.findByRole('heading', { name: 'Allocation by sector' })).toBeInTheDocument();
  });

  it('starts open on a tall window', async () => {
    window.innerHeight = 1000;
    renderPortfolio('?groupBy=sector');
    expect(await screen.findByRole('button', { name: 'Hide charts' })).toBeInTheDocument();
  });

  it('keeps what the owner chose, whatever the height', async () => {
    window.sessionStorage.setItem(storageKey, '0');
    window.innerHeight = 700;
    renderPortfolio('?groupBy=sector');
    expect(await screen.findByRole('button', { name: 'Hide charts' })).toBeInTheDocument();
  });

  it('says there are no groups when the filters leave none, and that none has a price when none has', async () => {
    server.use(http.get('/api/holdings/groups', () => HttpResponse.json(buildGroups({ groups: [] }))));
    const { unmount } = renderPortfolio('?groupBy=sector');
    expect(await screen.findByText('No groups to chart')).toBeInTheDocument();
    expect(screen.queryByText('Nothing to chart yet')).not.toBeInTheDocument();
    unmount();

    server.use(
      http.get('/api/holdings/groups', () =>
        HttpResponse.json(buildGroups({ groups: [buildGroup({ value: null, pnl: null, pnlPct: null, unpriced: 1 })] })),
      ),
    );
    renderPortfolio('?groupBy=sector');
    expect(await screen.findByText('Nothing to chart yet')).toBeInTheDocument();
    expect(screen.queryByText('No groups to chart')).not.toBeInTheDocument();
  });
});

describe('Group by: collapsing the Charts card', () => {
  it('collapses and expands, and remembers the choice for the session', async () => {
    const { unmount } = renderPortfolio('?groupBy=sector');
    await chartsCard();
    const hide = screen.getByRole('button', { name: 'Hide charts' });
    expect(hide).toHaveAttribute('aria-expanded', 'true');

    await userEvent.click(hide);

    const show = screen.getByRole('button', { name: 'Show charts' });
    expect(show).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByRole('heading', { name: 'Allocation by sector' })).not.toBeInTheDocument();
    expect(window.sessionStorage.getItem(storageKey)).toBe('1');
    unmount();

    // Another visit in the same session: the card opens collapsed.
    categoryStub.reset();
    renderPortfolio('?groupBy=account');
    expect(await screen.findByRole('button', { name: 'Show charts' })).toBeInTheDocument();
    await screen.findByRole('table', { name: 'Holdings by account' });
    scrollToEnd();
    expect(categoryStub.calls).toEqual([]);

    await userEvent.click(screen.getByRole('button', { name: 'Show charts' }));
    expect(window.sessionStorage.getItem(storageKey)).toBe('0');
    expect(await screen.findByRole('heading', { name: 'Allocation by account' })).toBeInTheDocument();
  });

  it('works when the browser refuses to store it', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    renderPortfolio('?groupBy=sector');
    await screen.findByRole('region', { name: 'Charts' });

    await userEvent.click(screen.getByRole('button', { name: 'Hide charts' }));

    expect(screen.getByRole('button', { name: 'Show charts' })).toBeInTheDocument();
    act(() => undefined);
  });
});
