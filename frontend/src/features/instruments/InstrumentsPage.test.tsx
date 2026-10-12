import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import {
  buildFacets,
  buildInstrument,
  buildInstrumentPage,
  buildRefreshStatus,
  sampleInstruments,
  searchInstruments,
} from '@/test/fixtures';
import { appliedCss } from '@/test/css';
import { scrollToEnd } from '@/test/intersection';
import { server } from '@/test/server';
import { smallPage, threePages } from '@/test/smallPages';
import { renderInstruments } from './testing';

// Lists page by a few rows here instead of 100 (the default of 100 is asserted in useInfiniteRows.test.tsx).
vi.mock('@/shared/lib/useInfiniteRows', async (importOriginal) =>
  (await import('@/test/smallPages')).withSmallPages(await importOriginal()),
);

const shown = (count: number, total = threePages) => `Showing ${String(count)} of ${String(total)}`;

const manyInstruments = (count: number, base: number, prefix: string) =>
  Array.from({ length: count }, (_, n) =>
    buildInstrument({
      id: `00000000-0000-4000-8000-00000000${String(base + n)}`,
      symbol: `NSE:${prefix}${String(n).padStart(3, '0')}`,
    }),
  );

const symbolsShown = () =>
  within(screen.getByRole('grid', { name: 'Instruments' }))
    .getAllByRole('row')
    .slice(1)
    .map((row) => within(row).getAllByRole('gridcell')[1]?.textContent);

const expectRows = (symbols: string[]) =>
  vi.waitFor(() => {
    expect(symbolsShown()).toEqual(symbols);
  });

async function grid() {
  return screen.findByRole('grid', { name: 'Instruments' });
}

describe('Instruments page: results', () => {
  it('lists the active instruments with their columns, chips and dashes for missing values', async () => {
    renderInstruments();

    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });
    expect(screen.getByRole('heading', { level: 1, name: 'Instruments' })).toBeInTheDocument();
    expect(symbolsShown()).toHaveLength(8); // the retired one is hidden by default
    expect(screen.queryByRole('gridcell', { name: 'NSE:OLDCO' })).not.toBeInTheDocument();
    const tiny = screen.getByRole('row', { name: /NSE:TINYCO/ });
    expect(within(tiny).getByText('SM')).toBeInTheDocument();
    expect(within(tiny).getByText('Tiny Co')).toBeInTheDocument();
    expect(within(tiny).getAllByRole('gridcell')[5]).toHaveTextContent('—'); // no sector
    expect(within(screen.getByRole('row', { name: /NSE:RELIANCE / })).getByText('F&O')).toBeInTheDocument();
    expect(within(screen.getByRole('row', { name: /NSE:YESBANK/ })).getByText('ASM/GSM')).toBeInTheDocument();
  });

  it('shows "Showing N of total" and loads the next page each time the list is scrolled to its end', async () => {
    const all = manyInstruments(threePages, 1000, 'S');
    const pages: string[] = [];
    server.use(
      http.get('/api/instruments', ({ request }) => {
        const params = new URL(request.url).searchParams;
        pages.push(`${params.get('page') ?? ''}/${params.get('pageSize') ?? ''}`);
        return HttpResponse.json(searchInstruments(all, params));
      }),
    );
    const { router } = renderInstruments();

    await screen.findByText(shown(smallPage));
    expect(screen.queryByRole('combobox', { name: 'Rows per page' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Go to next page' })).not.toBeInTheDocument();

    scrollToEnd();
    await screen.findByText(shown(smallPage * 2));
    expect(
      screen.getByRole('gridcell', { name: `NSE:S${String(smallPage * 2 - 1).padStart(3, '0')}` }),
    ).toBeInTheDocument();
    expect(screen.getByRole('gridcell', { name: 'NSE:S000' })).toBeInTheDocument();

    scrollToEnd();
    await screen.findByText(shown(threePages));
    expect(screen.getByRole('grid', { name: 'Instruments' })).toHaveAttribute('aria-rowcount', String(threePages + 1));
    expect(pages).toEqual([1, 2, 3].map((page) => `${String(page)}/${String(smallPage)}`)); // every page once
    expect(router.state.location.search).toBe(''); // no page or size in the URL
  });

  it('formats large totals with digit grouping', async () => {
    server.use(
      http.get('/api/instruments', () =>
        HttpResponse.json(buildInstrumentPage(sampleInstruments.slice(0, 2), { total: 3204 })),
      ),
    );
    renderInstruments();

    expect(await screen.findByText('Showing 2 of 3,204')).toBeInTheDocument();
  });

  it('ignores the page and size of an old link: the list always starts at the top, a full page of rows', async () => {
    const asked: string[] = [];
    server.use(
      http.get('/api/instruments', ({ request }) => {
        const params = new URL(request.url).searchParams;
        asked.push(`${params.get('page') ?? ''}/${params.get('pageSize') ?? ''}`);
        return HttpResponse.json(searchInstruments(sampleInstruments, params));
      }),
    );
    renderInstruments('?page=9&size=50');

    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });
    expect(asked).toEqual([`1/${String(smallPage)}`]);
  });

  it('shows skeleton rows on the first load', async () => {
    server.use(http.get('/api/instruments', async () => new Promise(() => undefined)));
    renderInstruments();

    expect(await grid()).toHaveAttribute('aria-busy', 'true');
    expect(screen.queryByRole('gridcell', { name: /^NSE:/ })).not.toBeInTheDocument();
    expect(document.querySelectorAll('.MuiSkeleton-root').length).toBeGreaterThan(0);
  });

  it('keeps the old rows, dimmed, while a new sort order loads, and then starts the new list from its top', async () => {
    const all = manyInstruments(threePages, 2000, 'P');
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const asked: string[] = [];
    server.use(
      http.get('/api/instruments', async ({ request }) => {
        const params = new URL(request.url).searchParams;
        asked.push(`${params.get('sort') ?? ''}:${params.get('page') ?? ''}`);
        if (params.get('sort') === '-symbol') await gate;
        return HttpResponse.json(searchInstruments(all, params));
      }),
    );
    renderInstruments();
    await screen.findByRole('gridcell', { name: 'NSE:P000' });
    scrollToEnd();
    await screen.findByText(shown(smallPage * 2));

    await userEvent.click(screen.getByRole('button', { name: 'Symbol' }));

    // The new order is on its way: the old rows are still there, the grid says it is busy and the list is dimmed.
    await vi.waitFor(() => {
      expect(screen.getByRole('grid', { name: 'Instruments' })).toHaveAttribute('aria-busy', 'true');
    });
    expect(screen.getByRole('gridcell', { name: 'NSE:P000' })).toBeInTheDocument();
    expect(screen.getByRole('grid', { name: 'Instruments' }).parentElement).toHaveStyle({ opacity: '0.6' });
    expect(document.querySelectorAll('.MuiSkeleton-root')).toHaveLength(0);
    scrollToEnd(); // more of the old list is not requested
    expect(asked.filter((a) => a.endsWith(':3'))).toEqual([]);

    release();
    await screen.findByRole('gridcell', { name: `NSE:P${String(threePages - 1).padStart(3, '0')}` });
    expect(screen.getByText(shown(smallPage))).toBeInTheDocument(); // a new list: its first page only
    expect(screen.queryByRole('gridcell', { name: 'NSE:P000' })).not.toBeInTheDocument();
    expect(screen.getByRole('grid', { name: 'Instruments' })).toHaveAttribute('aria-busy', 'false');
    expect(screen.getByRole('grid', { name: 'Instruments' }).parentElement).toHaveStyle({ opacity: '1' });
  });

  it('keeps the loaded rows and offers a retry when a further page cannot be loaded', async () => {
    const all = manyInstruments(threePages, 3000, 'E');
    let fail = true;
    server.use(
      http.get('/api/instruments', ({ request }) => {
        const params = new URL(request.url).searchParams;
        return params.get('page') === '2' && fail
          ? HttpResponse.json({ title: 'Search is unavailable' }, { status: 503 })
          : HttpResponse.json(searchInstruments(all, params));
      }),
    );
    renderInstruments();
    await screen.findByText(shown(smallPage));

    scrollToEnd();

    expect(await screen.findByText('Could not load more rows')).toBeInTheDocument();
    expect(
      screen.getByRole('gridcell', { name: `NSE:E${String(smallPage - 1).padStart(3, '0')}` }),
    ).toBeInTheDocument();
    scrollToEnd(); // no automatic retry
    fail = false;
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));

    await screen.findByText(shown(smallPage * 2));
    expect(screen.queryByText('Could not load more rows')).not.toBeInTheDocument();
  });

  it('hides Series and Sector (and Tick) on narrow screens; the detail drawer still shows them', async () => {
    renderInstruments();
    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });

    const hiddenBelow = (name: string) =>
      appliedCss(screen.getByRole('columnheader', { name })).responsive.map((r) => `${r.query} ${r.css}`);
    expect(hiddenBelow('Series')).toEqual(['(min-width:0px) display: none;', '(min-width:900px) display: table-cell;']);
    expect(hiddenBelow('Sector')).toEqual(hiddenBelow('Series'));
    expect(hiddenBelow('Tick')[1]).toMatch(/min-width:1536px/);
    expect(hiddenBelow('Symbol')).toEqual([]);

    await userEvent.click(screen.getByRole('gridcell', { name: 'NSE:RELIANCE' }));
    const drawer = await screen.findByRole('dialog', { name: 'Instrument details' });
    expect(await within(drawer).findByText('Series', { selector: 'dt' })).toBeInTheDocument();
    expect(within(drawer).getByText('Sector', { selector: 'dt' })).toBeInTheDocument();
  });

  it('says so and offers to clear the filters when nothing matches', async () => {
    const { router } = renderInstruments('?q=zzzz');

    expect(await screen.findByText('No instruments match these filters')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Clear filters' }));

    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });
    expect(router.state.location.search).toBe('');
  });

  it('shows an error with a retry when the search fails', async () => {
    let fail = true;
    server.use(
      http.get('/api/instruments', ({ request }) =>
        fail
          ? HttpResponse.json({ title: 'Search is unavailable' }, { status: 503 })
          : HttpResponse.json(searchInstruments(sampleInstruments, new URL(request.url).searchParams)),
      ),
    );
    renderInstruments();

    expect(await screen.findByText('Search is unavailable')).toBeInTheDocument();
    fail = false;
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));

    expect(await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' })).toBeInTheDocument();
  });
});

describe('Instruments page: search and filters', () => {
  it('searches after a pause in typing, matching symbol, name or ISIN, and writes it to the URL', async () => {
    const seen: string[] = [];
    server.use(
      http.get('/api/instruments', ({ request }) => {
        const params = new URL(request.url).searchParams;
        seen.push(params.get('q') ?? '');
        return HttpResponse.json(searchInstruments(sampleInstruments, params));
      }),
    );
    const { router } = renderInstruments();
    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });

    await userEvent.type(screen.getByRole('searchbox', { name: 'Search instruments' }), 'INE467');

    await screen.findByRole('gridcell', { name: 'NSE:TCS' });
    await expectRows(['NSE:TCS']);
    expect(router.state.location.search).toBe('?q=INE467');
    expect(seen).toEqual(['', 'INE467']); // one request for the whole word, not one per key
  });

  it('has the search placeholder and fills the box from the URL', async () => {
    renderInstruments('?q=hdfc');

    const box = await screen.findByRole('searchbox', { name: 'Search instruments' });
    expect(box).toHaveValue('hdfc');
    expect(box).toHaveAttribute('placeholder', 'Search symbol, name or ISIN');
    await screen.findByRole('gridcell', { name: 'NSE:HDFCBANK' });
    await expectRows(['NSE:HDFCBANK']);
  });

  it('filters by type with counts on the chips, and toggles it off again', async () => {
    const { router } = renderInstruments();

    const etf = await screen.findByRole('button', { name: 'ETF 1' });
    expect(screen.getByRole('button', { name: 'Share 5' })).toBeInTheDocument();
    expect(etf).toHaveAttribute('aria-pressed', 'false');
    await userEvent.click(etf);

    await screen.findByRole('gridcell', { name: 'NSE:NIFTYBEES' });
    await expectRows(['NSE:NIFTYBEES']);
    expect(router.state.location.search).toBe('?type=etf');
    expect(screen.getByRole('button', { name: 'ETF 1' })).toHaveAttribute('aria-pressed', 'true');

    await userEvent.click(screen.getByRole('button', { name: 'ETF 1' }));
    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });
    expect(router.state.location.search).toBe('');
  });

  it('filters by sector with a type-ahead: counts in the options, Unclassified included, URL updated', async () => {
    const { router } = renderInstruments();
    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });

    await userEvent.click(screen.getByRole('combobox', { name: 'Sector' }));
    expect(screen.getAllByRole('option').map((o) => o.getAttribute('aria-label'))).toEqual([
      'Financial Services 2',
      'Information Technology 1',
      'Oil Gas & Consumable Fuels 1',
      'Unclassified 4',
    ]);
    await userEvent.click(screen.getByRole('option', { name: 'Unclassified 4' }));

    await screen.findByRole('gridcell', { name: 'NSE:TINYCO' });
    await vi.waitFor(() => {
      expect(symbolsShown()).toHaveLength(4);
    });
    expect(router.state.location.search).toBe('?sector=Unclassified');
    expect(screen.getByRole('combobox', { name: 'Sector' })).toHaveValue('Unclassified');
  });

  it('narrows the sector list as you type, highlights the match, and clears the sector', async () => {
    const { router } = renderInstruments('?sector=Unclassified');
    await vi.waitFor(() => {
      expect(symbolsShown()).toHaveLength(4);
    });
    expect(screen.getByRole('combobox', { name: 'Sector' })).toHaveValue('Unclassified');

    await userEvent.clear(screen.getByRole('combobox', { name: 'Sector' })); // emptying the text clears the filter
    await vi.waitFor(() => {
      expect(router.state.location.search).toBe('');
    });
    await userEvent.type(screen.getByRole('combobox', { name: 'Sector' }), 'tech');

    const option = screen.getByRole('option');
    expect(option).toHaveAccessibleName('Information Technology 1');
    expect(option.querySelector('mark')).toHaveTextContent('Tech');
    await userEvent.keyboard('{ArrowDown}{Enter}');
    await vi.waitFor(() => {
      expect(router.state.location.search).toBe('?sector=Information+Technology');
    });
    await vi.waitFor(() => {
      expect(symbolsShown()).toEqual(['NSE:TCS']);
    });
  });

  it('says so when no sector matches what is typed', async () => {
    renderInstruments();
    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });

    await userEvent.type(screen.getByRole('combobox', { name: 'Sector' }), 'zzz');

    expect(screen.getByText('No sector matches')).toBeInTheDocument();
  });

  it('filters by series', async () => {
    renderInstruments();
    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });

    await userEvent.click(screen.getByRole('combobox', { name: 'Series' }));
    await userEvent.click(screen.getByRole('option', { name: 'SM (1)' }));

    await screen.findByRole('gridcell', { name: 'NSE:TINYCO' });
    await expectRows(['NSE:TINYCO']);
  });

  it('filters by F&O and ASM/GSM, and combines the filters', async () => {
    const { router } = renderInstruments();
    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });

    await userEvent.click(screen.getByRole('button', { name: 'F&O only 3' }));
    await vi.waitFor(() => {
      expect(router.state.location.search).toContain('fno=1');
    });

    await userEvent.click(screen.getByRole('button', { name: 'ASM/GSM only 1' }));
    await screen.findByText('No instruments match these filters'); // no F&O share is under ASM/GSM

    await userEvent.click(screen.getByRole('button', { name: 'F&O only 3' }));
    await screen.findByRole('gridcell', { name: 'NSE:YESBANK' });
    await expectRows(['NSE:YESBANK']);
    expect(router.state.location.search).toBe('?asm=1');
  });

  it('shows retired instruments only on request', async () => {
    const { router } = renderInstruments();
    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });

    await userEvent.click(screen.getByRole('switch', { name: 'Show retired (1)' }));

    expect(await screen.findByRole('gridcell', { name: 'NSE:OLDCO' })).toBeInTheDocument();
    expect(within(screen.getByRole('row', { name: /NSE:OLDCO/ })).getByText('Retired')).toBeInTheDocument();
    expect(router.state.location.search).toBe('?retired=1');
  });

  it('clears every filter at once, keeping the sort', async () => {
    const { router } = renderInstruments('?q=bank&type=share&fno=1&sort=-name');
    await screen.findByRole('gridcell', { name: 'NSE:HDFCBANK' });

    await userEvent.click(screen.getByRole('button', { name: 'Clear all filters' }));

    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });
    expect(router.state.location.search).toBe('?sort=-name');
    expect(screen.getByRole('searchbox', { name: 'Search instruments' })).toHaveValue('');
  });

  it('keeps working when the counts cannot be loaded', async () => {
    server.use(http.get('/api/instruments/facets', () => HttpResponse.json({ title: 'No counts' }, { status: 500 })));
    renderInstruments();

    expect(await screen.findByRole('button', { name: 'Share' })).toBeInTheDocument();
    expect(screen.getByRole('gridcell', { name: 'NSE:RELIANCE' })).toBeInTheDocument();
  });

  it('goes back through the history of filters', async () => {
    const { router } = renderInstruments();
    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });
    await userEvent.click(screen.getByRole('button', { name: 'ETF 1' }));
    await screen.findByRole('gridcell', { name: 'NSE:NIFTYBEES' });

    await router.navigate(-1);

    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });
    expect(screen.getByRole('button', { name: 'ETF 1' })).toHaveAttribute('aria-pressed', 'false');
  });
});

describe('Instruments page: sorting', () => {
  it('marks the sorted column with aria-sort and flips the direction on a second click', async () => {
    const { router } = renderInstruments();
    await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });
    expect(screen.getByRole('columnheader', { name: 'Symbol' })).toHaveAttribute('aria-sort', 'ascending');
    expect(screen.getByRole('columnheader', { name: 'Lot' })).not.toHaveAttribute('aria-sort');

    await userEvent.click(screen.getByRole('button', { name: 'Lot' }));
    await screen.findByRole('columnheader', { name: 'Lot', selected: undefined });
    expect(screen.getByRole('columnheader', { name: 'Lot' })).toHaveAttribute('aria-sort', 'ascending');
    expect(router.state.location.search).toBe('?sort=lot');

    await userEvent.click(screen.getByRole('button', { name: 'Lot' }));
    expect(await screen.findByRole('columnheader', { name: 'Lot' })).toHaveAttribute('aria-sort', 'descending');
    expect(router.state.location.search).toBe('?sort=-lot');
    await vi.waitFor(() => {
      expect(symbolsShown()[0]).toBe('NSE:RELIANCE-2026-10-FUT'); // lot 500 first
    });
  });
});

describe('Instruments page: detail drawer', () => {
  it('opens the details of a row, shows all fields, and closes again', async () => {
    const { router } = renderInstruments();

    await userEvent.click(await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' }));

    const drawer = await screen.findByRole('dialog', { name: 'Instrument details' });
    expect(await within(drawer).findByText('INE002A01018')).toBeInTheDocument();
    const term = (name: string) => within(drawer).getByText(name, { selector: 'dt' }).nextElementSibling?.textContent;
    expect(term('Dhan id')).toBe('2885 (NSE_EQ)');
    expect(term('Sector')).toBe('Oil Gas & Consumable Fuels — from NSE Nifty Total Market list');
    expect(term('Lot size')).toBe('1');
    expect(term('MTF leverage')).toBe('4.5x');
    expect(term('Freeze quantity')).toBe('81,195');
    expect(term('First seen')).toMatch(/Oct 2026/);
    expect(term('Last seen')).toMatch(/Oct 2026/);
    expect(router.state.location.search).toMatch(/^\?id=/);

    await userEvent.click(within(drawer).getByRole('button', { name: 'Close details' }));
    await vi.waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Instrument details' })).not.toBeInTheDocument();
    });
    expect(router.state.location.search).toBe('');
  });

  it('opens from a click anywhere on the row, and from a shared link', async () => {
    renderInstruments();
    await userEvent.click(await screen.findByText('Tata Consultancy Services'));
    expect(await screen.findByRole('dialog', { name: 'Instrument details' })).toBeInTheDocument();
  });

  it('shows an error with a retry when the detail cannot be loaded', async () => {
    server.use(http.get('/api/instruments/:id', () => HttpResponse.json({ title: 'Detail failed' }, { status: 500 })));
    renderInstruments();

    await userEvent.click(await screen.findByRole('gridcell', { name: 'NSE:TCS' }));

    const drawer = await screen.findByRole('dialog', { name: 'Instrument details' });
    expect(await within(drawer).findByText('Detail failed')).toBeInTheDocument();
    expect(within(drawer).getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });

  it('explains a retired ASM/GSM instrument in words', async () => {
    renderInstruments('?retired=1');
    await userEvent.click(await screen.findByRole('gridcell', { name: 'NSE:YESBANK' }));

    const drawer = await screen.findByRole('dialog', { name: 'Instrument details' });
    expect(await within(drawer).findByText('Yes — Y · ASM Stage 1')).toBeInTheDocument();
  });
});

describe('Instruments page: empty master', () => {
  it('shows only the call to action when there is no master list yet', async () => {
    server.use(
      http.get('/api/instruments/refresh/status', () =>
        HttpResponse.json(buildRefreshStatus({ last: null, totalActive: 0 })),
      ),
      http.get('/api/instruments/facets', () => HttpResponse.json(buildFacets([]))),
    );
    renderInstruments();

    expect(await screen.findByText('No master list yet — refresh it to get started')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Refresh master list' })).toBeEnabled();
    expect(screen.queryByRole('searchbox')).not.toBeInTheDocument();
    expect(screen.queryByRole('grid')).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Master list' })).not.toBeInTheDocument(); // still on request only
  });
});
