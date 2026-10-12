import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { delay, http, HttpResponse } from 'msw';
import { buildDataJob, buildDataOverview, buildDhanPhase, buildGapRow, sampleGaps } from '@/test/fixtures';
import { createDataFake } from '@/test/dataFake';
import { scrollToEnd } from '@/test/intersection';
import { smallPage, threePages } from '@/test/smallPages';
import { gapColumns } from './components/gapColumns';
import { openStep, renderData } from './testing';

// Lists page by a few rows here instead of 100 (the default of 100 is asserted in useInfiniteRows.test.tsx).
vi.mock('@/shared/lib/useInfiniteRows', async (importOriginal) =>
  (await import('@/test/smallPages')).withSmallPages(await importOriginal()),
);

const dhanStep = () => openStep('Dhan Data API');
const panel = () => screen.findByRole('tabpanel', { name: /^Gaps/ });
const gapsGrid = async () => within(await panel()).findByRole('grid', { name: 'Gaps' });
const rowsIn = (grid: HTMLElement) => within(grid).getAllByRole('row').slice(1);
const withGaps = (gaps = sampleGaps) => createDataFake({ gaps });

const manyGaps = (count = threePages) =>
  Array.from({ length: count }, (_, n) =>
    buildGapRow({
      date: `2026-09-${String(30 - (n % 30)).padStart(2, '0')}`,
      symbol: `NSE:S${String(n).padStart(3, '0')}`,
      instrumentId: `00000000-0000-4000-8000-00000000${String(3000 + n)}`,
    }),
  );

describe('Gaps: the Dhan step', () => {
  it('says how many sessions have missing bars now, in the chip and in the details', async () => {
    renderData({ fake: withGaps() });

    const chip = await screen.findByRole('listitem', { name: /Dhan Data API/ });
    expect(await within(chip).findByText('4,500 bars added · 3 open · 1 unfillable')).toBeInTheDocument();
    const dhan = await dhanStep();
    expect(within(dhan).getByText('Gaps: 3 open · 1 unfillable')).toBeInTheDocument();
    expect(within(dhan).getByText(/^Last found/)).toBeInTheDocument();
    expect(within(dhan).getByRole('button', { name: 'View gaps' })).toBeInTheDocument();
  });

  it('says when there are none, and offers no list', async () => {
    renderData();

    const dhan = await dhanStep();
    expect(within(dhan).getByText('Gaps: No open gaps')).toBeInTheDocument();
    expect(within(dhan).queryByRole('button', { name: 'View gaps' })).not.toBeInTheDocument();
  });

  it('with only unfillable gaps still names them', async () => {
    renderData({ fake: withGaps(sampleGaps.filter((g) => g.status === 'unfillable')) });

    expect(await within(await dhanStep()).findByText('Gaps: 1 unfillable')).toBeInTheDocument();
  });

  it('shows what the last run found, filled and could not fill', async () => {
    const job = buildDataJob({ dhan: buildDhanPhase({ gapsFound: 5, gapsFilled: 3, gapsUnfillable: 1 }) });
    renderData({
      fake: createDataFake({
        overview: buildDataOverview({ lastJobs: { csv: job, dhan: job, aggregate: job } }),
      }),
    });

    expect(
      await within(await dhanStep()).findByText('Gaps in this run: 5 found · 3 filled · 1 unfillable · 1 still open'),
    ).toBeInTheDocument();
  });

  it('says nothing about the run when it found no gaps', async () => {
    renderData();

    expect(within(await dhanStep()).queryByText(/Gaps in this run/)).not.toBeInTheDocument();
  });

  it('View gaps closes the details and opens the Gaps tab on the open gaps', async () => {
    const { router } = renderData({ fake: withGaps() });

    await userEvent.click(within(await dhanStep()).getByRole('button', { name: 'View gaps' }));

    expect(await screen.findByRole('tab', { name: /^Gaps/ })).toHaveAttribute('aria-selected', 'true');
    expect(screen.queryByRole('dialog', { name: 'Dhan Data API details' })).not.toBeInTheDocument();
    expect(router.state.location.search).toBe('?tab=gaps&gaps=open');
    await waitFor(async () => {
      expect(rowsIn(await gapsGrid())).toHaveLength(3);
    });
  });

  it('with only filled and unfillable gaps, View gaps shows all of them', async () => {
    const { router } = renderData({ fake: withGaps(sampleGaps.filter((g) => g.status !== 'open')) });

    await userEvent.click(within(await dhanStep()).getByRole('button', { name: 'View gaps' }));

    await waitFor(() => {
      expect(router.state.location.search).toBe('?tab=gaps&gaps=all');
    });
  });
});

describe('Gaps: the tab', () => {
  it('lists the open gaps: instrument, day, found against expected, attempts, state, last attempt', async () => {
    const fake = withGaps();
    const { router } = renderData({ fake, search: '?tab=gaps' });

    const grid = await gapsGrid();
    await waitFor(() => {
      expect(rowsIn(grid)).toHaveLength(3);
    });
    const [first] = rowsIn(grid);
    expect(first).toHaveTextContent('NSE:INFY');
    expect(first).toHaveTextContent('INFOSYS LTD');
    expect(first).toHaveTextContent('9 Oct 2026');
    expect(first).toHaveTextContent('120 / 375');
    expect(first).toHaveTextContent('Open');
    expect(first).toHaveTextContent('9 Oct 2026, 15:52 IST');
    expect(within(first!).getByTestId('WarningAmberOutlinedIcon')).toBeInTheDocument();
    expect(rowsIn(grid)[2]).toHaveTextContent('0 / 375');
    expect(rowsIn(grid)[2]).toHaveTextContent('—');
    expect(fake.calls.gaps.at(-1)).toContain('status=open');
    expect(router.state.location.search).toBe('?tab=gaps');
  });

  it('opens from the old link ?gaps=open on the Gaps tab, with the Gaps tab selected', async () => {
    const { router } = renderData({ fake: withGaps(), search: '?gaps=open' });

    expect(await screen.findByRole('tab', { name: /^Gaps/ })).toHaveAttribute('aria-selected', 'true');
    expect(await gapsGrid()).toBeInTheDocument();
    // The tab is written to the URL, so that clearing the filter later does not change the tab.
    await waitFor(() => {
      expect(router.state.location.search).toBe('?gaps=open&tab=gaps');
    });
  });

  it('opens on the filter named by the link: ?gaps=unfillable', async () => {
    const fake = withGaps();
    renderData({ fake, search: '?gaps=unfillable' });

    const grid = await gapsGrid();
    await waitFor(() => {
      expect(rowsIn(grid)).toHaveLength(1);
    });
    expect(grid).toHaveTextContent('NSE:TCS');
    expect(grid).toHaveTextContent('Unfillable');
    expect(screen.getByRole('button', { name: /^Unfillable/ })).toHaveAttribute('aria-pressed', 'true');
  });

  it('filters by state with the counts on the chips, and "All" asks for no state', async () => {
    const fake = withGaps();
    const { router } = renderData({ fake, search: '?tab=gaps' });
    const chips = within(await panel()).getByRole('group', { name: 'Gap status' });
    await within(await panel()).findAllByRole('gridcell', { name: 'NSE:INFY' });

    expect(
      within(chips)
        .getAllByRole('button')
        .map((chip) => chip.textContent),
    ).toEqual(['Open 3', 'Unfillable 1', 'Filled 1', 'All 5']);
    expect(within(chips).getByRole('button', { name: 'Open 3' })).toHaveAttribute('aria-pressed', 'true');

    await userEvent.click(within(chips).getByRole('button', { name: 'Unfillable 1' }));
    await waitFor(async () => {
      expect(rowsIn(await gapsGrid())).toHaveLength(1);
    });
    expect(fake.calls.gaps.at(-1)).toContain('status=unfillable');
    expect(router.state.location.search).toBe('?tab=gaps&gaps=unfillable');

    await userEvent.click(within(chips).getByRole('button', { name: 'All 5' }));
    await waitFor(async () => {
      expect(rowsIn(await gapsGrid())).toHaveLength(5);
    });
    expect(fake.calls.gaps.at(-1)).not.toContain('status=');
    expect(await gapsGrid()).toHaveTextContent('Filled');
  });

  it('explains the list behind an info button', async () => {
    renderData({ fake: withGaps(), search: '?tab=gaps' });
    await gapsGrid();

    await userEvent.click(screen.getByRole('button', { name: 'About gaps' }));

    expect(
      await screen.findByText(/Sessions of tracked instruments with missing 1-minute bars\. "Re-fetch gaps"/),
    ).toBeInTheDocument();
  });

  it('says so when there is nothing to list', async () => {
    renderData({ fake: withGaps(sampleGaps.filter((g) => g.status === 'filled')), search: '?tab=gaps' });

    expect(await within(await panel()).findByText('No open gaps.')).toBeInTheDocument();
  });

  it('shows a loading state while the list loads', async () => {
    renderData({
      fake: withGaps(),
      search: '?tab=gaps',
      handlers: [
        http.get('/api/data/gaps', async () => {
          await delay('infinite');
          return HttpResponse.json({});
        }),
      ],
    });

    expect(await gapsGrid()).toHaveAttribute('aria-busy', 'true');
  });

  it('says in words when the list cannot be loaded, and loads it on Retry', async () => {
    const fake = withGaps();
    fake.failures.gaps = true;
    renderData({ fake, search: '?tab=gaps' });
    const gaps = await panel();

    expect(await within(gaps).findByText('The data service is not available.')).toBeInTheDocument();

    fake.failures.gaps = false;
    await userEvent.click(within(gaps).getByRole('button', { name: 'Retry' }));
    expect(await within(gaps).findAllByRole('gridcell', { name: 'NSE:INFY' })).toHaveLength(3);
  });

  it('hides name, attempts and the last attempt on narrow screens', () => {
    const hidden = Object.fromEntries(gapColumns.map((column) => [column.id, column.hideBelow]));

    expect(hidden).toEqual({
      symbol: undefined,
      name: 'md',
      date: undefined,
      bars: undefined,
      attempts: 'sm',
      status: undefined,
      lastAttempt: 'md',
    });
  });
});

describe('Gaps: infinite scroll', () => {
  const shown = (count: number) => `Showing ${String(count)} of ${String(threePages)}`;

  it('shows "Showing N of total" and loads the next page at the end of the list', async () => {
    const fake = withGaps(manyGaps());
    renderData({ fake, search: '?gaps=all' });
    await screen.findByText(shown(smallPage));

    scrollToEnd();
    await screen.findByText(shown(smallPage * 2));
    scrollToEnd();
    await screen.findByText(shown(threePages));

    expect(fake.calls.gaps.map((call) => new URLSearchParams(call).get('page'))).toEqual(['1', '2', '3']);
    expect(fake.calls.gaps.every((call) => call.includes(`pageSize=${String(smallPage)}`))).toBe(true);
  });

  it('starts the new list from its first page when the filter changes', async () => {
    const fake = withGaps(manyGaps());
    renderData({ fake, search: '?gaps=all' });
    await screen.findByText(shown(smallPage));
    scrollToEnd();
    await screen.findByText(shown(smallPage * 2));

    await userEvent.click(screen.getByRole('button', { name: /^Open/ }));

    await screen.findByText(shown(smallPage));
    expect(fake.calls.gaps.at(-1)).toContain('page=1');
    expect(fake.calls.gaps.at(-1)).toContain('status=open');
  });

  it('keeps the loaded rows and offers a retry when a further page cannot be loaded', async () => {
    const fake = withGaps(manyGaps());
    renderData({ fake, search: '?gaps=all' });
    await screen.findByText(shown(smallPage));

    fake.failures.gaps = true;
    scrollToEnd();
    expect(await screen.findByText('Could not load more rows')).toBeInTheDocument();
    fake.failures.gaps = false;
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));

    await screen.findByText(shown(smallPage * 2));
  });
});
