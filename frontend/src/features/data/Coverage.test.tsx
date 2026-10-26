import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { delay, http } from 'msw';
import { appliedCss } from '@/test/css';
import { buildBarsOverview, buildCoverageRow, buildDataOverview, sampleCoverage } from '@/test/fixtures';
import { createDataFake } from '@/test/dataFake';
import { scrollToEnd } from '@/test/intersection';
import { smallPage, threePages } from '@/test/smallPages';
import { renderData } from './testing';
import { coverageColumns } from './components/coverageColumns';

// Lists page by a few rows here instead of 100 (the default of 100 is asserted in useInfiniteRows.test.tsx).
vi.mock('@/shared/lib/useInfiniteRows', async (importOriginal) =>
  (await import('@/test/smallPages')).withSmallPages(await importOriginal()),
);

const shown = (count: number) => `Showing ${String(count)} of ${String(threePages)}`;
const symbolOf = (n: number) => `NSE:S${String(n).padStart(3, '0')}`;

/** The table with its first rows loaded. */
const grid = () => screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });
const symbolsShown = () =>
  within(screen.getByRole('grid', { name: 'Coverage' }))
    .getAllByRole('row')
    .slice(1)
    .map((row) => within(row).getAllByRole('gridcell')[0]?.textContent);
const expectRows = (symbols: string[]) =>
  waitFor(() => {
    expect(symbolsShown()).toEqual(symbols);
  });
const rowOf = (symbol: string) => screen.getByRole('row', { name: new RegExp(`${symbol}( |$)`) });

const manyCoverage = (count: number, prefix = 'S') =>
  Array.from({ length: count }, (_, n) =>
    buildCoverageRow({
      id: `00000000-0000-4000-8000-00000000${String(2000 + n)}`,
      symbol: `NSE:${prefix}${String(n).padStart(3, '0')}`,
      name: `Company ${String(n)}`,
    }),
  );

/** The counts of the five sample instruments: 3 current, 1 behind, 1 empty. */
const sampleOverview = () =>
  buildDataOverview({ bars: buildBarsOverview({ instruments: 4, instrumentsBehind: 1, instrumentsEmpty: 1 }) });
const fakeOfSample = () => createDataFake({ overview: sampleOverview() });

describe('Coverage list: rows', () => {
  it('lists each instrument with last bar, bars, source and status in India time; the first bar is the symbol’s tooltip', async () => {
    renderData();
    await grid();

    expect(symbolsShown()).toEqual(['NSE:HDFCBANK', 'NSE:INFY', 'NSE:NEWCO', 'NSE:RELIANCE', 'NSE:TCS']);
    const reliance = rowOf('NSE:RELIANCE');
    expect(within(reliance).getByText('RELIANCE INDUSTRIES LTD')).toBeInTheDocument();
    expect(within(reliance).getByTitle('First bar 1 Apr 2024, 09:15 IST')).toHaveTextContent('NSE:RELIANCE');
    expect(within(reliance).queryByText('1 Apr 2024, 09:15 IST')).not.toBeInTheDocument();
    expect(within(reliance).getByText('12 Oct 2026, 15:29 IST')).toBeInTheDocument();
    expect(within(reliance).getByText('1,87,500')).toBeInTheDocument();
    expect(within(reliance).getByText('CSV')).toBeInTheDocument();
    expect(within(reliance).getByText('Current')).toBeInTheDocument();
    expect(within(rowOf('NSE:TCS')).getByText('CSV + Dhan')).toBeInTheDocument();
    expect(within(rowOf('NSE:HDFCBANK')).getByText('Dhan')).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Last bar (IST)' })).toBeInTheDocument();
    expect(screen.queryByRole('columnheader', { name: /First bar/ })).not.toBeInTheDocument();
  });

  it('highlights a last bar that is behind, with the status in words and an icon as well', async () => {
    renderData();
    await grid();

    const infy = rowOf('NSE:INFY');
    expect(within(infy).getByText('Behind 2 d')).toBeInTheDocument();
    expect(within(infy).getByTestId('WarningAmberOutlinedIcon')).toBeInTheDocument();
    const behindBar = within(infy).getByText('8 Oct 2026, 15:29 IST').parentElement;
    const currentBar = within(rowOf('NSE:RELIANCE')).getByText('12 Oct 2026, 15:29 IST').parentElement;
    expect(behindBar).toHaveStyle({ fontWeight: '700' });
    expect(currentBar).not.toHaveStyle({ fontWeight: '700' });
    expect(within(rowOf('NSE:RELIANCE')).getByTestId('CheckCircleOutlinedIcon')).toBeInTheDocument();
  });

  it('shows dashes and "Empty" for a tracked instrument without bars', async () => {
    renderData();
    await grid();

    const newco = rowOf('NSE:NEWCO');
    expect(within(newco).getByText('Empty')).toBeInTheDocument();
    expect(within(newco).getAllByText('—').length).toBeGreaterThanOrEqual(2);
    expect(within(newco).getByTitle('No bars yet')).toBeInTheDocument();
  });

  it('says so when no instrument has data, and when the list cannot be loaded (Retry loads it)', async () => {
    const fake = createDataFake({ coverage: [] });
    fake.failures.coverage = true;
    renderData({ fake });

    expect(await screen.findByText('The data service is not available.')).toBeInTheDocument();
    fake.failures.coverage = false;
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByText('No instruments with data yet.')).toBeInTheDocument();
  });

  it('chooses the columns by width: symbol and status always, last bar from sm, name and bars from md, source and gaps from lg', async () => {
    const hiddenBelow = Object.fromEntries(coverageColumns.map((c) => [c.id, c.hideBelow ?? 'always']));
    expect(hiddenBelow).toEqual({
      symbol: 'always',
      name: 'md',
      lastBar: 'sm',
      bars: 'md',
      source: 'lg',
      gaps: 'lg',
      status: 'always',
    });

    // And the grid really applies them: the sortable header of a hidden column is shown only from its breakpoint.
    renderData();
    await grid();
    const queries = (name: string) =>
      appliedCss(screen.getByRole('columnheader', { name })).responsive.map((r) => `${r.query} ${r.css}`);
    expect(queries('Symbol')).toEqual([]);
    expect(queries('Last bar (IST)')).toEqual([
      '(min-width:0px) display: none;',
      '(min-width:600px) display: table-cell;',
    ]);
    expect(queries('1m bars')[1]).toMatch(/min-width:900px/);
    expect(queries('Gaps')[1]).toMatch(/min-width:1200px/);
  });

  it('is a grid that fills its tab: no pager, a footer that says how many rows are shown', async () => {
    renderData();
    await grid();

    expect(screen.queryByRole('combobox', { name: 'Rows per page' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /page/i })).not.toBeInTheDocument();
    expect(await screen.findByText('Showing 5 of 5')).toBeInTheDocument();
  });
});

describe('Coverage list: search, filter and sort (all in the URL)', () => {
  it('searches symbol and name', async () => {
    const { router, fake } = renderData();
    await grid();

    await userEvent.type(screen.getByRole('searchbox', { name: 'Search coverage' }), 'tata');

    await expectRows(['NSE:TCS']);
    expect(router.state.location.search).toBe('?q=tata');
    expect(fake.calls.coverage.at(-1)).toContain('q=tata');
  });

  it('shows the status chips with the counts of all instruments and filters by one, and clears all filters at once', async () => {
    const { router } = renderData({ fake: fakeOfSample() });
    await grid();

    const chips = screen.getByRole('group', { name: 'Status' });
    expect(
      within(chips)
        .getAllByRole('button')
        .map((chip) => chip.textContent),
    ).toEqual(['All 5', 'Current 3', 'Behind 1', 'Empty 1']);
    expect(within(chips).getByRole('button', { name: 'All 5' })).toHaveAttribute('aria-pressed', 'true');

    await userEvent.click(within(chips).getByRole('button', { name: 'Behind 1' }));

    await expectRows(['NSE:INFY']);
    expect(router.state.location.search).toBe('?status=behind');
    expect(within(chips).getByRole('button', { name: 'Behind 1' })).toHaveAttribute('aria-pressed', 'true');

    await userEvent.click(screen.getByRole('button', { name: 'Clear filters' }));
    await expectRows(['NSE:HDFCBANK', 'NSE:INFY', 'NSE:NEWCO', 'NSE:RELIANCE', 'NSE:TCS']);
    expect(router.state.location.search).toBe('');
    expect(screen.getByRole('button', { name: 'Clear filters' })).toBeDisabled();
  });

  it('shows the open gaps of an instrument as a chip with a word, and a dash for none', async () => {
    renderData();
    await grid();

    const infy = rowOf('NSE:INFY');
    expect(within(infy).getByText('3 open')).toBeInTheDocument();
    expect(within(infy).getByTestId('PlaylistRemoveOutlinedIcon')).toBeInTheDocument();
    expect(within(rowOf('NSE:RELIANCE')).queryByText(/open$/)).not.toBeInTheDocument();
  });

  it('filters to the instruments that have gaps, in the URL', async () => {
    const { router, fake } = renderData();
    await grid();

    const toggle = screen.getByRole('button', { name: 'Has gaps' });
    expect(toggle).toHaveAttribute('aria-pressed', 'false');
    await userEvent.click(toggle);

    await expectRows(['NSE:INFY']);
    expect(router.state.location.search).toBe('?hasGaps=true');
    expect(fake.calls.coverage.at(-1)).toContain('hasGaps=true');
    expect(screen.getByRole('button', { name: 'Has gaps' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button', { name: 'Clear filters' })).toBeEnabled();

    await userEvent.click(screen.getByRole('button', { name: 'Clear filters' }));
    await expectRows(['NSE:HDFCBANK', 'NSE:INFY', 'NSE:NEWCO', 'NSE:RELIANCE', 'NSE:TCS']);
    expect(screen.getByRole('button', { name: 'Has gaps' })).toHaveAttribute('aria-pressed', 'false');
    expect(router.state.location.search).toBe('');
  });

  it('reads the Has gaps filter from the URL', async () => {
    const { fake } = renderData({ search: '?hasGaps=true' });

    await expectRows(['NSE:INFY']);
    expect(screen.getByRole('button', { name: 'Has gaps' })).toHaveAttribute('aria-pressed', 'true');
    expect(fake.calls.coverage[0]).toContain('hasGaps=true');
  });

  it('offers to clear the filters when nothing matches', async () => {
    renderData({ search: '?q=zzz' });

    expect(await screen.findByText('No instruments match these filters.')).toBeInTheDocument();
    const message = screen.getByText('No instruments match these filters.');
    await userEvent.click(within(message.parentElement!).getByRole('button', { name: 'Clear filters' }));
    await expectRows(['NSE:HDFCBANK', 'NSE:INFY', 'NSE:NEWCO', 'NSE:RELIANCE', 'NSE:TCS']);
  });

  it('starts from the filters in a shared link', async () => {
    renderData({ search: '?status=current&sort=-bars', fake: fakeOfSample() });

    await expectRows(['NSE:RELIANCE', 'NSE:HDFCBANK', 'NSE:TCS']);
    expect(screen.getByRole('button', { name: 'Current 3' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('columnheader', { name: '1m bars' })).toHaveAttribute('aria-sort', 'descending');
  });

  it('sorts by the last bar, oldest first and then newest first', async () => {
    const { router } = renderData();
    await grid();
    expect(screen.getByRole('columnheader', { name: 'Symbol' })).toHaveAttribute('aria-sort', 'ascending');

    await userEvent.click(screen.getByRole('button', { name: 'Last bar (IST)' }));

    await waitFor(() => {
      expect(symbolsShown().slice(0, 2)).toEqual(['NSE:NEWCO', 'NSE:INFY']);
    });
    expect(screen.getByRole('columnheader', { name: 'Last bar (IST)' })).toHaveAttribute('aria-sort', 'ascending');
    expect(router.state.location.search).toBe('?sort=lastBar');

    await userEvent.click(screen.getByRole('button', { name: 'Last bar (IST)' }));

    await waitFor(() => {
      expect(symbolsShown().at(-1)).toBe('NSE:NEWCO');
    });
    expect(symbolsShown().at(-2)).toBe('NSE:INFY');
    expect(router.state.location.search).toBe('?sort=-lastBar');
  });
});

describe('Coverage list: infinite scroll', () => {
  it('shows "Showing N of total" and loads the next page each time the list is scrolled to its end, with nothing about pages in the URL', async () => {
    const fake = createDataFake({ coverage: manyCoverage(threePages) });
    const { router } = renderData({ fake });
    await screen.findByText(shown(smallPage));

    scrollToEnd();
    await screen.findByText(shown(smallPage * 2));
    expect(screen.getByRole('gridcell', { name: symbolOf(smallPage * 2 - 1) })).toBeInTheDocument();
    expect(screen.getByRole('gridcell', { name: symbolOf(0) })).toBeInTheDocument();

    scrollToEnd();
    await screen.findByText(shown(threePages));
    expect(screen.getByRole('grid', { name: 'Coverage' })).toHaveAttribute('aria-rowcount', String(threePages + 1));
    expect(fake.calls.coverage.map((call) => new URLSearchParams(call).get('page'))).toEqual(['1', '2', '3']);
    expect(fake.calls.coverage.every((call) => call.includes(`pageSize=${String(smallPage)}`))).toBe(true);
    expect(router.state.location.search).toBe('');
  });

  it('starts a new list from the top when a filter changes, and does not ask for more of the old one', async () => {
    const fake = createDataFake({ coverage: manyCoverage(threePages) });
    renderData({ fake });
    await screen.findByText(shown(smallPage));
    scrollToEnd();
    await screen.findByText(shown(smallPage * 2));

    await userEvent.type(screen.getByRole('searchbox', { name: 'Search coverage' }), 'S02');

    await screen.findByText('Showing 5 of 5'); // S020 to S024
    expect(fake.calls.coverage.at(-1)).toContain('page=1');
    expect(symbolsShown()).toHaveLength(5);
  });

  it('ignores the page and size of an old link: the list starts at the top, a full page of rows', async () => {
    const fake = createDataFake({ coverage: manyCoverage(threePages) });
    renderData({ search: '?page=9&size=50', fake });

    await screen.findByText(shown(smallPage));
    expect(fake.calls.coverage).toHaveLength(1);
    expect(fake.calls.coverage[0]).toContain('page=1');
    expect(fake.calls.coverage[0]).toContain(`pageSize=${String(smallPage)}`);
  });

  it('keeps the loaded rows and offers a retry when a further page cannot be loaded', async () => {
    const fake = createDataFake({ coverage: manyCoverage(threePages) });
    renderData({ fake });
    await screen.findByText(shown(smallPage));

    fake.failures.coverage = true;
    scrollToEnd();

    expect(await screen.findByText('Could not load more rows')).toBeInTheDocument();
    expect(screen.getByRole('gridcell', { name: symbolOf(smallPage - 1) })).toBeInTheDocument();
    fake.failures.coverage = false;
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
    await screen.findByText(shown(smallPage * 2));
  });
});

describe('Coverage table: the bars drawer', () => {
  const openReliance = async () => {
    const result = renderData();
    await userEvent.click(await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' }));
    return { ...result, drawer: await screen.findByRole('dialog', { name: 'Bars' }) };
  };

  it('opens from a row with the latest 1-minute bars, newest first, with their source', async () => {
    const { router, fake, drawer } = await openReliance();

    expect(await within(drawer).findByRole('heading', { name: 'NSE:RELIANCE' })).toBeInTheDocument();
    expect(within(drawer).getByText('RELIANCE INDUSTRIES LTD')).toBeInTheDocument();
    expect(within(drawer).getByRole('combobox', { name: 'Timeframe' })).toHaveTextContent('1 minute');
    const table = await within(drawer).findByRole('table', { name: 'Latest 1m bars' });
    const rows = within(table).getAllByRole('row');
    expect(rows).toHaveLength(4);
    expect(
      within(table)
        .getAllByRole('columnheader')
        .map((cell) => cell.textContent),
    ).toEqual(['Time (IST)', 'Open', 'High', 'Low', 'Close', 'Volume', 'Source']);
    expect(rows[1]).toHaveTextContent('15:29, 12 Oct');
    expect(rows[1]).toHaveTextContent('2,456.30');
    expect(rows[1]).toHaveTextContent('2,460.00');
    expect(rows[1]).toHaveTextContent('2,455.15');
    expect(rows[1]).toHaveTextContent('2,458.90');
    expect(rows[1]).toHaveTextContent('12,345');
    expect(rows[1]).toHaveTextContent('CSV');
    expect(rows[2]).toHaveTextContent('15:28, 12 Oct');
    expect(router.state.location.search).toMatch(/^\?id=/);
    expect(fake.calls.bars).toEqual([{ id: sampleCoverage[0]?.id, timeframe: '1m', take: '100' }]);
  });

  it('shows another timeframe on request: bars it was built from and whether each is complete, in words', async () => {
    const { router, fake, drawer } = await openReliance();
    await within(drawer).findByRole('table', { name: 'Latest 1m bars' });

    await userEvent.click(within(drawer).getByRole('combobox', { name: 'Timeframe' }));
    await userEvent.click(await screen.findByRole('option', { name: '1 day' }));

    const table = await within(drawer).findByRole('table', { name: 'Latest 1D bars' });
    expect(
      within(table)
        .getAllByRole('columnheader')
        .map((cell) => cell.textContent),
    ).toEqual(['Time (IST)', 'Open', 'High', 'Low', 'Close', 'Volume', '1m bars', 'Complete']);
    const rows = within(table).getAllByRole('row');
    expect(rows[1]).toHaveTextContent('12 Oct 2026');
    expect(rows[1]).toHaveTextContent('375');
    expect(rows[1]).toHaveTextContent('Complete');
    expect(rows[2]).toHaveTextContent('11 Oct 2026');
    expect(rows[2]).toHaveTextContent('Partial');
    expect(router.state.location.search).toMatch(/&tf=1D$/);
    expect(fake.calls.bars.map((call) => call.timeframe)).toEqual(['1m', '1D']);
  });

  it('opens at the timeframe of a shared link', async () => {
    const id = sampleCoverage[0]?.id ?? '';
    renderData({ search: `?id=${id}&tf=15m` });

    const drawer = await screen.findByRole('dialog', { name: 'Bars' });
    expect(within(drawer).getByRole('combobox', { name: 'Timeframe' })).toHaveTextContent('15 minutes');
    expect(await within(drawer).findByRole('table', { name: 'Latest 15m bars' })).toBeInTheDocument();
  });

  it('says when a timeframe has no bars yet', async () => {
    const { drawer } = await openReliance();
    await within(drawer).findByRole('table', { name: 'Latest 1m bars' });

    await userEvent.click(within(drawer).getByRole('combobox', { name: 'Timeframe' }));
    await userEvent.click(await screen.findByRole('option', { name: '1 month' }));

    expect(
      await within(drawer).findByText(
        /No 1 month bars yet for this instrument\. Rebuild the aggregates to create them\./,
      ),
    ).toBeInTheDocument();
  });

  it('shows a loading state while the bars are read', async () => {
    renderData({
      handlers: [
        http.get('/api/data/instruments/:id/bars', async () => {
          await delay('infinite');
        }),
      ],
    });
    await userEvent.click(await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' }));
    const drawer = await screen.findByRole('dialog', { name: 'Bars' });

    expect(within(drawer).getByText('Loading bars')).toBeInTheDocument();
  });

  it('shows an error with a retry when the bars cannot be loaded', async () => {
    const fake = createDataFake();
    fake.failures.bars = true;
    renderData({ fake });
    await userEvent.click(await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' }));
    const drawer = await screen.findByRole('dialog', { name: 'Bars' });

    expect(await within(drawer).findByText('The data service is not available.')).toBeInTheDocument();

    fake.failures.bars = false;
    await userEvent.click(within(drawer).getByRole('button', { name: 'Retry' }));
    expect(await within(drawer).findByRole('table', { name: 'Latest 1m bars' })).toBeInTheDocument();
  });

  it('closes and takes the instrument out of the URL', async () => {
    const { router, drawer } = await openReliance();

    await userEvent.click(within(drawer).getByRole('button', { name: 'Close bars' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Bars' })).not.toBeInTheDocument();
    });
    expect(router.state.location.search).toBe('');
  });
});
