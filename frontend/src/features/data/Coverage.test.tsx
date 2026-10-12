import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { delay, http } from 'msw';
import { buildCoverageRow, sampleCoverage } from '@/test/fixtures';
import { createDataFake } from '@/test/dataFake';
import { renderData } from './testing';
import { coverageColumns } from './components/coverageColumns';

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

const bigCoverage = Array.from({ length: 60 }, (_, n) =>
  buildCoverageRow({
    id: `00000000-0000-4000-8000-00000000${String(2000 + n)}`,
    symbol: `NSE:S${String(n).padStart(3, '0')}`,
    name: `Company ${String(n)}`,
  }),
);

describe('Coverage table: rows', () => {
  it('lists each instrument with first bar, last bar, bars, source and status in India time', async () => {
    renderData();
    await grid();

    expect(symbolsShown()).toEqual(['NSE:HDFCBANK', 'NSE:INFY', 'NSE:NEWCO', 'NSE:RELIANCE', 'NSE:TCS']);
    const reliance = rowOf('NSE:RELIANCE');
    expect(within(reliance).getByText('RELIANCE INDUSTRIES LTD')).toBeInTheDocument();
    expect(within(reliance).getByText('1 Apr 2024, 09:15 IST')).toBeInTheDocument();
    expect(within(reliance).getByText('12 Oct 2026, 15:29 IST')).toBeInTheDocument();
    expect(within(reliance).getByText('1,87,500')).toBeInTheDocument();
    expect(within(reliance).getByText('CSV')).toBeInTheDocument();
    expect(within(reliance).getByText('Current')).toBeInTheDocument();
    expect(within(rowOf('NSE:TCS')).getByText('CSV + Dhan')).toBeInTheDocument();
    expect(within(rowOf('NSE:HDFCBANK')).getByText('Dhan')).toBeInTheDocument();
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
    expect(within(newco).getAllByText('—').length).toBeGreaterThanOrEqual(3);
  });

  it('says so when no instrument has data, and when the table cannot be loaded (Retry loads it)', async () => {
    const fake = createDataFake({ coverage: [] });
    fake.failures.coverage = true;
    renderData({ fake });

    expect(await screen.findByText('The data service is not available.')).toBeInTheDocument();
    fake.failures.coverage = false;
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByText('No instruments with data yet.')).toBeInTheDocument();
  });

  it('chooses the columns by width: symbol and status always, last bar from sm, name and bars from md, the rest from lg', () => {
    const hiddenBelow = Object.fromEntries(coverageColumns.map((c) => [c.id, c.hideBelow ?? 'always']));
    expect(hiddenBelow).toEqual({
      symbol: 'always',
      name: 'md',
      firstBar: 'lg',
      lastBar: 'sm',
      bars: 'md',
      source: 'lg',
      status: 'always',
    });
  });
});

describe('Coverage table: search, filter, sort and paging (all in the URL)', () => {
  it('searches symbol and name', async () => {
    const { router, fake } = renderData();
    await grid();

    await userEvent.type(screen.getByRole('searchbox', { name: 'Search coverage' }), 'tata');

    await expectRows(['NSE:TCS']);
    expect(router.state.location.search).toBe('?q=tata');
    expect(fake.calls.coverage.at(-1)).toContain('q=tata');
  });

  it('filters by status, and clears all filters at once', async () => {
    const { router } = renderData();
    await grid();

    await userEvent.click(screen.getByRole('combobox', { name: 'Status' }));
    await userEvent.click(await screen.findByRole('option', { name: 'Behind' }));

    await expectRows(['NSE:INFY']);
    expect(router.state.location.search).toBe('?status=behind');

    await userEvent.click(screen.getByRole('button', { name: 'Clear filters' }));
    await expectRows(['NSE:HDFCBANK', 'NSE:INFY', 'NSE:NEWCO', 'NSE:RELIANCE', 'NSE:TCS']);
    expect(router.state.location.search).toBe('');
    expect(screen.getByRole('button', { name: 'Clear filters' })).toBeDisabled();
  });

  it('offers to clear the filters when nothing matches', async () => {
    renderData({ search: '?q=zzz' });

    expect(await screen.findByText('No instruments match these filters.')).toBeInTheDocument();
    const message = screen.getByText('No instruments match these filters.');
    await userEvent.click(within(message.parentElement!).getByRole('button', { name: 'Clear filters' }));
    await expectRows(['NSE:HDFCBANK', 'NSE:INFY', 'NSE:NEWCO', 'NSE:RELIANCE', 'NSE:TCS']);
  });

  it('starts from the filters in a shared link', async () => {
    renderData({ search: '?status=current&sort=-bars' });

    await expectRows(['NSE:RELIANCE', 'NSE:HDFCBANK', 'NSE:TCS']);
    expect(screen.getByRole('combobox', { name: 'Status' })).toHaveTextContent('Current');
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

  it('pages through the rows, with the page and size in the URL', async () => {
    const { router } = renderData({ fake: createDataFake({ coverage: bigCoverage }) });
    await screen.findByRole('gridcell', { name: 'NSE:S000' });
    await expectRows(bigCoverage.slice(0, 25).map((row) => row.symbol));
    expect(screen.getByText('1–25 of 60')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Go to next page' }));

    await expectRows(bigCoverage.slice(25, 50).map((row) => row.symbol));
    expect(screen.getByText('26–50 of 60')).toBeInTheDocument();
    expect(router.state.location.search).toBe('?page=2');

    await userEvent.click(screen.getByRole('combobox', { name: 'Rows per page' }));
    await userEvent.click(await screen.findByRole('option', { name: '50' }));

    await expectRows(bigCoverage.slice(0, 50).map((row) => row.symbol));
    expect(router.state.location.search).toBe('?size=50');
  });

  it('steps back to the last page when a link points past it', async () => {
    const { router } = renderData({ search: '?page=9', fake: createDataFake({ coverage: bigCoverage }) });

    await expectRows(bigCoverage.slice(50).map((row) => row.symbol));
    expect(router.state.location.search).toBe('?page=3');
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

    expect(within(drawer).getByRole('progressbar', { name: 'Loading bars' })).toBeInTheDocument();
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
