import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { scrollToEnd } from '@/test/intersection';
import { buildQualityRow, qualityId, sampleQualityRows } from '@/test/qualityFixtures';
import { createQualityFake } from '@/test/qualityFake';
import { smallPage, threePages } from '@/test/smallPages';
import { qualityColumns } from './components/qualityColumns';
import { renderQuality } from './testing';

// Lists page by a few rows here instead of 100 (the default of 100 is asserted in useInfiniteRows.test.tsx).
vi.mock('@/shared/lib/useInfiniteRows', async (importOriginal) =>
  (await import('@/test/smallPages')).withSmallPages(await importOriginal()),
);

const shown = (count: number) => `Showing ${String(count)} of ${String(threePages)}`;
const first = () => screen.findByRole('gridcell', { name: 'NSE:RELIANCE' });
const symbolsShown = () =>
  within(screen.getByRole('grid', { name: 'Instruments' }))
    .getAllByRole('row')
    .slice(1)
    .map((row) => within(row).getAllByRole('gridcell')[0]?.textContent);
const expectRows = (symbols: string[]) =>
  waitFor(() => {
    expect(symbolsShown()).toEqual(symbols);
  });
const rowOf = (symbol: string) => screen.getByRole('row', { name: new RegExp(`${symbol}( |$)`) });
/** Worst first, then by symbol: the default order. */
const defaultOrder = ['NSE:HDFCBANK', 'NSE:INFY', 'NSE:TCS', 'NSE:RELIANCE', 'NSE:NEWCO'];

/** `count` instruments; every fifth one fails, so a status filter has something to narrow to. */
const manyRows = (count: number) =>
  Array.from({ length: count }, (_, n) =>
    buildQualityRow({
      instrumentId: qualityId(10 + n),
      symbol: `NSE:S${String(n).padStart(3, '0')}`,
      name: `Company ${String(n)}`,
      ...(n % 5 === 4 ? { status: 'fail', score: 75 } : {}),
    }),
  );
const chip = (name: string | RegExp) => screen.findByRole('button', { name });

describe('Instruments table: rows', () => {
  it('lists each instrument with its status, score, coverage, missing minutes and time of the check', async () => {
    renderQuality();
    await first();

    expect(symbolsShown()).toEqual(defaultOrder);
    const reliance = rowOf('NSE:RELIANCE');
    expect(within(reliance).getByText('RELIANCE INDUSTRIES LTD')).toBeInTheDocument();
    expect(within(reliance).getAllByText('Pass').length).toBeGreaterThanOrEqual(1);
    expect(within(reliance).getByText('100')).toBeInTheDocument();
    expect(within(reliance).getByText('100.00%')).toBeInTheDocument();
    expect(within(reliance).getByText('12 Oct 2026, 15:52 IST')).toBeInTheDocument();
    const tcs = rowOf('NSE:TCS');
    expect(within(tcs).getByText('98.40%')).toBeInTheDocument();
    expect(within(tcs).getByText('30')).toBeInTheDocument();
  });

  it('leaves the bars checked and the source to the Inspect drawer', async () => {
    renderQuality();
    await first();

    expect(screen.queryByRole('columnheader', { name: /bars checked/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('columnheader', { name: 'Source' })).not.toBeInTheDocument();
    expect(within(rowOf('NSE:RELIANCE')).queryByText('1,875')).not.toBeInTheDocument();
    expect(within(rowOf('NSE:TCS')).queryByText('CSV + Dhan')).not.toBeInTheDocument();
  });

  it('says the status in words with an icon as well, never by colour alone', async () => {
    renderQuality();
    await first();

    const infy = rowOf('NSE:INFY');
    expect(within(infy).getAllByText('Fail').length).toBeGreaterThanOrEqual(1);
    expect(within(infy).getAllByTestId('ErrorOutlineOutlinedIcon').length).toBeGreaterThanOrEqual(1);
    expect(within(rowOf('NSE:TCS')).getAllByText('Warning').length).toBeGreaterThanOrEqual(1);
    expect(within(rowOf('NSE:TCS')).getAllByTestId('WarningAmberOutlinedIcon').length).toBeGreaterThanOrEqual(1);
    expect(within(rowOf('NSE:RELIANCE')).getAllByTestId('CheckCircleOutlinedIcon').length).toBeGreaterThanOrEqual(1);
    const newco = rowOf('NSE:NEWCO');
    expect(within(newco).getByText('Not checked')).toBeInTheDocument();
    expect(within(newco).getAllByText('—').length).toBeGreaterThanOrEqual(4);
  });

  it('has no aggregates column (the marks per timeframe are in the Inspect drawer)', async () => {
    renderQuality();
    await first();

    expect(screen.queryByRole('columnheader', { name: 'Aggregates' })).not.toBeInTheDocument();
    expect(within(rowOf('NSE:INFY')).queryByRole('img', { hidden: true })).not.toBeInTheDocument();
  });

  it('chooses the columns by width: symbol, status and score on a phone; the rest from md; the time of the check from xl', () => {
    const hiddenBelow = Object.fromEntries(qualityColumns.map((c) => [c.id, c.hideBelow ?? 'always']));
    expect(hiddenBelow).toEqual({
      symbol: 'always',
      name: 'md',
      status: 'always',
      score: 'always',
      coverage: 'md',
      missingMinutes: 'md',
      checkedAt: 'xl',
    });
  });

  it('says so when no tracked instrument has bars, and when the table cannot be loaded (Retry loads it)', async () => {
    const fake = createQualityFake({ rows: [] });
    fake.failures.instruments = true;
    renderQuality({ fake });

    expect(await screen.findByText('The data service is not available.')).toBeInTheDocument();
    fake.failures.instruments = false;
    await userEvent.click(
      within(screen.getByRole('region', { name: 'Instruments' })).getByRole('button', { name: 'Retry' }),
    );
    expect(await screen.findByText('No tracked instrument has bars to check yet.')).toBeInTheDocument();
  });
});

describe('Instruments table: search, filter and sort (all in the URL)', () => {
  it('starts with the worst first, which the sorted header says', async () => {
    const { router } = renderQuality();
    await first();

    expect(screen.getByRole('columnheader', { name: 'Status' })).toHaveAttribute('aria-sort', 'descending');
    expect(router.state.location.search).toBe('');
  });

  it('searches symbol and name', async () => {
    const { router, fake } = renderQuality();
    await first();

    await userEvent.type(screen.getByRole('searchbox', { name: 'Search instruments' }), 'tata');

    await expectRows(['NSE:TCS']);
    expect(router.state.location.search).toBe('?q=tata');
    expect(fake.calls.instruments.at(-1)).toContain('q=tata');
  });

  it('filters by status, and clears all filters at once', async () => {
    const { router, fake } = renderQuality();
    await first();

    await userEvent.click(await chip('Failed 2'));

    await expectRows(['NSE:HDFCBANK', 'NSE:INFY']);
    expect(router.state.location.search).toBe('?status=fail');
    expect(fake.calls.instruments.at(-1)).toContain('status=fail');
    expect(await chip('Failed 2')).toHaveAttribute('aria-pressed', 'true');

    await userEvent.click(screen.getByRole('button', { name: 'Clear filters' }));
    await expectRows(defaultOrder);
    expect(router.state.location.search).toBe('');
    expect(screen.queryByRole('button', { name: 'Clear filters' })).not.toBeInTheDocument();
  });

  it('can filter to the instruments never checked', async () => {
    const { router } = renderQuality();
    await first();

    await userEvent.click(await chip('Not checked 1'));

    await expectRows(['NSE:NEWCO']);
    expect(router.state.location.search).toBe('?status=notChecked');
  });

  it('starts from the filters in a shared link', async () => {
    renderQuality({ search: '?status=warning&sort=-coverage' });

    await expectRows(['NSE:TCS']);
    expect(await chip('Warnings 1')).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('columnheader', { name: 'Coverage' })).toHaveAttribute('aria-sort', 'descending');
  });

  it('offers to clear the filters when nothing matches', async () => {
    renderQuality({ search: '?q=zzz' });

    const message = await screen.findByText('No instruments match these filters.');
    await userEvent.click(within(message.parentElement!).getByRole('button', { name: 'Clear filters' }));
    await expectRows(defaultOrder);
  });

  it('sorts by score, lowest first and then highest first', async () => {
    const { router } = renderQuality();
    await first();

    await userEvent.click(screen.getByRole('button', { name: 'Score' }));

    await expectRows(['NSE:NEWCO', 'NSE:INFY', 'NSE:HDFCBANK', 'NSE:TCS', 'NSE:RELIANCE']);
    expect(screen.getByRole('columnheader', { name: 'Score' })).toHaveAttribute('aria-sort', 'ascending');
    expect(router.state.location.search).toBe('?sort=score');

    await userEvent.click(screen.getByRole('button', { name: 'Score' }));

    await expectRows(['NSE:RELIANCE', 'NSE:TCS', 'NSE:HDFCBANK', 'NSE:INFY', 'NSE:NEWCO']);
    expect(router.state.location.search).toBe('?sort=-score');
  });

  it('sorts by symbol', async () => {
    const { router } = renderQuality();
    await first();

    await userEvent.click(screen.getByRole('button', { name: 'Symbol' }));

    await expectRows(['NSE:HDFCBANK', 'NSE:INFY', 'NSE:NEWCO', 'NSE:RELIANCE', 'NSE:TCS']);
    expect(router.state.location.search).toBe('?sort=symbol');
  });

  it('ignores the page and size of an old link', async () => {
    const { router } = renderQuality({ search: '?page=9&size=50&status=warning' });

    await expectRows(['NSE:TCS']);
    await userEvent.click(screen.getByRole('button', { name: 'Symbol' }));
    await waitFor(() => {
      expect(router.state.location.search).toBe('?status=warning&sort=symbol');
    });
  });

  it('keeps the sample rows complete', () => {
    expect(sampleQualityRows.map((r) => r.status)).toEqual(['pass', 'warning', 'fail', 'fail', 'notChecked']);
  });
});

describe('Instruments table: infinite scroll', () => {
  const symbolsOf = (rows: readonly { symbol: string }[]) => rows.map((row) => row.symbol);

  it('loads a page of rows at a time as the list is scrolled, says "Showing N of total" and keeps page and size out of the URL', async () => {
    const rows = manyRows(threePages);
    const { router, fake } = renderQuality({ fake: createQualityFake({ rows }) });

    await screen.findByText(shown(smallPage));
    expect(screen.queryByRole('combobox', { name: 'Rows per page' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Go to next page' })).not.toBeInTheDocument();
    expect(screen.getByRole('grid', { name: 'Instruments' })).toHaveAttribute('aria-rowcount', String(threePages + 1));

    scrollToEnd();
    await screen.findByText(shown(smallPage * 2));
    scrollToEnd();
    await screen.findByText(shown(threePages));

    // Every page once, a page at a time, worst first.
    expect(fake.calls.instruments.map((q) => new URLSearchParams(q).get('page'))).toEqual(['1', '2', '3']);
    expect(fake.calls.instruments.every((q) => new URLSearchParams(q).get('pageSize') === String(smallPage))).toBe(
      true,
    );
    expect(router.state.location.search).toBe('');
    expect(symbolsShown()).toHaveLength(threePages);
    expect(new Set(symbolsShown()).size).toBe(threePages);
    const failed = symbolsOf(rows.filter((r) => r.status === 'fail')).sort();
    expect(symbolsShown().slice(0, failed.length)).toEqual(failed);
  });

  it('starts a new list at the first page when a result chip changes the filter', async () => {
    const { router, fake } = renderQuality({ fake: createQualityFake({ rows: manyRows(threePages) }) });
    await screen.findByText(shown(smallPage));
    scrollToEnd();
    await screen.findByText(shown(smallPage * 2));

    await userEvent.click(await chip(`Failed ${String(threePages / 5)}`));

    await screen.findByText(`Showing ${String(threePages / 5)} of ${String(threePages / 5)}`);
    expect(router.state.location.search).toBe('?status=fail');
    const last = new URLSearchParams(fake.calls.instruments.at(-1));
    expect([last.get('status'), last.get('page')]).toEqual(['fail', '1']);
    expect(symbolsShown()).toHaveLength(threePages / 5);
  });

  it('starts a new list at the first page when the sort or the search changes', async () => {
    const { fake } = renderQuality({ fake: createQualityFake({ rows: manyRows(threePages) }) });
    await screen.findByText(shown(smallPage));
    scrollToEnd();
    await screen.findByText(shown(smallPage * 2));

    await userEvent.click(screen.getByRole('button', { name: 'Symbol' }));

    await waitFor(() => {
      expect(new URLSearchParams(fake.calls.instruments.at(-1)).get('sort')).toBe('symbol');
    });
    await waitFor(() => {
      expect(symbolsShown()).toHaveLength(smallPage);
    });
    expect(symbolsShown()[0]).toBe('NSE:S000');
    expect(new URLSearchParams(fake.calls.instruments.at(-1)).get('page')).toBe('1');

    await userEvent.type(screen.getByRole('searchbox', { name: 'Search instruments' }), 'Company 2');
    await screen.findByText('Showing 6 of 6'); // Company 2 and 20 to 24
    expect(new URLSearchParams(fake.calls.instruments.at(-1)).get('page')).toBe('1');
  });

  it('says it could not load more rows and loads them when Retry is pressed', async () => {
    const fake = createQualityFake({ rows: manyRows(threePages) });
    renderQuality({ fake });
    await screen.findByText(shown(smallPage));

    fake.failures.instruments = true;
    scrollToEnd();
    expect(await screen.findByText('Could not load more rows')).toBeInTheDocument();
    expect(screen.getByText(shown(smallPage))).toBeInTheDocument();

    fake.failures.instruments = false;
    await userEvent.click(
      within(screen.getByRole('grid', { name: 'Instruments' })).getByRole('button', { name: 'Retry' }),
    );

    await screen.findByText(shown(smallPage * 2));
    expect(screen.queryByText('Could not load more rows')).not.toBeInTheDocument();
  });

  it('shows skeleton rows while the first page loads', async () => {
    renderQuality();

    expect(await screen.findByRole('grid', { name: 'Instruments', busy: true })).toBeInTheDocument();
    await first();
  });
});
