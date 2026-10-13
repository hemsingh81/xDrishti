import { act, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { delay, http, HttpResponse } from 'msw';
import { buildBucket, buildSummary, ids } from '@/test/fixtures';
import { setNow } from '@/test/clock';
import { drawnSeries } from '@/test/chartStub';
import {
  buildEmptyHistory,
  buildHistory,
  buildHistoryPoint,
  buildHistoryBenchmark,
  buildTrackResult,
  buildUnavailableBenchmark,
  sampleHistoryPoints,
} from '@/test/historyFixtures';
import { scrollToEnd } from '@/test/intersection';
import { stubPhoneWidth } from '@/test/matchMedia';
import { problemResponse, server } from '@/test/server';
import { captureHistory, historyCard, noHoldingsHandlers, renderPortfolio, revealChart } from './testing';

/** Tuesday 13 Oct 2026, 10:35 in India. */
const sessionNow = '2026-10-13T05:05:00Z';

const summaryWith = (overrides: Parameters<typeof buildSummary>[0]) =>
  http.get('/api/holdings/summary', () => HttpResponse.json(buildSummary(overrides)));

const rangeButton = (name: string) =>
  within(screen.getByRole('group', { name: 'History range' })).getByRole('button', { name });

describe('History card', () => {
  it('shows the card under the totals with the chips (All is chosen), the three lines and the chart', async () => {
    const requests = captureHistory();
    renderPortfolio();

    const card = await historyCard();
    expect(within(card).getByRole('heading', { name: 'History' })).toBeInTheDocument();
    expect(rangeButton('All')).toHaveAttribute('aria-pressed', 'true');
    for (const chip of ['1M', '3M', '6M', '1Y']) expect(rangeButton(chip)).toHaveAttribute('aria-pressed', 'false');

    await revealChart();
    expect(drawnSeries().map((s) => s.name)).toEqual(['Value', 'Invested', 'Nifty 50 (same cash flows)']);
    expect(
      within(card).getByRole('img', { name: /Portfolio value history, 6 dates from 1 Jan 2025 to 9 Oct 2026/ }),
    ).toBeInTheDocument();
    // All: no range is sent, and never a resolution (the server picks).
    expect(requests[0]?.toString()).toBe('');
  });

  it('shows a skeleton of the chart while the history loads', async () => {
    server.use(http.get('/api/portfolio/history', () => new Promise<never>(() => undefined)));
    renderPortfolio();

    const card = await historyCard();
    expect(await within(card).findByRole('status', { name: 'Loading chart' })).toBeInTheDocument();
    expect(screen.queryByTestId('chart-canvas')).not.toBeInTheDocument();
  });

  it('says what went wrong and retries', async () => {
    let attempts = 0;
    server.use(
      http.get('/api/portfolio/history', () => {
        attempts += 1;
        return attempts === 1
          ? problemResponse({ title: 'History failed.', code: 'portfolio.invalid' }, 500)
          : HttpResponse.json(buildHistory());
      }),
    );
    renderPortfolio();

    const card = await historyCard();
    expect(await within(card).findByText('History failed.')).toBeInTheDocument();
    expect(screen.queryByTestId('chart-canvas')).not.toBeInTheDocument();

    await userEvent.click(within(card).getByRole('button', { name: 'Retry' }));

    await revealChart();
    expect(within(card).queryByText('History failed.')).not.toBeInTheDocument();
  });

  it("shows the server's words for a scope that is too large, with no Retry and no advice of ours (a chip cannot clear it)", async () => {
    server.use(
      http.get('/api/portfolio/history', () =>
        problemResponse(
          {
            title: 'Invalid request.',
            status: 400,
            code: 'portfolio.invalid',
            errors: {
              scope: ['Too many price bars to read at once for this scope. Pick one account or bucket.'],
            },
          },
          400,
        ),
      ),
    );
    renderPortfolio();

    const card = await historyCard();
    expect(
      await within(card).findByText('Too many price bars to read at once for this scope. Pick one account or bucket.'),
    ).toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument();
    expect(within(card).queryByText(/Choose a shorter range/)).not.toBeInTheDocument();
  });

  it('says "No history yet" when nothing has price history, with the way to build it', async () => {
    captureHistory(buildEmptyHistory());
    renderPortfolio();

    const card = await historyCard();
    expect(await within(card).findByText('No history yet')).toBeInTheDocument();
    expect(within(card).getByText(/Track your holdings and run Update data/)).toBeInTheDocument();
    expect(screen.queryByTestId('chart-canvas')).not.toBeInTheDocument();
    // The coverage line still offers the action.
    expect(within(card).getByRole('button', { name: 'Track my holdings' })).toBeInTheDocument();
  });

  it('is not shown when there are no holdings at all', async () => {
    server.use(...noHoldingsHandlers());
    renderPortfolio();

    expect(await screen.findByRole('grid', { name: 'Holdings' })).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'History' })).not.toBeInTheDocument();
  });

  it('loads the chart only when the card is about to be seen', async () => {
    renderPortfolio();
    const card = await historyCard();
    await within(card).findByRole('list', { name: 'History coverage' });
    expect(within(card).getByRole('status', { name: 'Loading chart' })).toBeInTheDocument();
    expect(screen.queryByTestId('chart-canvas')).not.toBeInTheDocument();

    scrollToEnd();

    expect(await screen.findByTestId('chart-canvas')).toBeInTheDocument();
  });
});

describe('History card: range', () => {
  it.each([
    ['1M', '2026-09-13'],
    ['3M', '2026-07-13'],
    ['6M', '2026-04-13'],
    ['1Y', '2025-10-13'],
  ])('asks for %s counted back from today in India', async (chip, from) => {
    setNow(sessionNow);
    const requests = captureHistory();
    renderPortfolio();
    await revealChart();

    await userEvent.click(rangeButton(chip));

    await vi.waitFor(() => {
      expect(requests.at(-1)?.get('from')).toBe(from);
    });
    expect(requests.at(-1)?.get('to')).toBe('2026-10-13');
    expect(requests.at(-1)?.has('resolution')).toBe(false);
    expect(rangeButton(chip)).toHaveAttribute('aria-pressed', 'true');
  });

  it('counts today by the India calendar: just after midnight IST is already the next day', async () => {
    setNow('2026-10-12T19:00:00Z'); // 00:30 on 13 Oct in India, still 12 Oct in UTC
    const requests = captureHistory();
    renderPortfolio();
    await revealChart();

    await userEvent.click(rangeButton('1M'));

    await vi.waitFor(() => {
      expect(requests.at(-1)?.get('to')).toBe('2026-10-13');
    });
    expect(requests.at(-1)?.get('from')).toBe('2026-09-13');
  });

  it('goes back to the whole history with All', async () => {
    setNow(sessionNow);
    const requests = captureHistory();
    renderPortfolio();
    await revealChart();
    await userEvent.click(rangeButton('1Y'));
    await vi.waitFor(() => {
      expect(requests.at(-1)?.has('from')).toBe(true);
    });

    await userEvent.click(rangeButton('All'));

    await vi.waitFor(() => {
      expect(requests.at(-1)?.has('from')).toBe(false);
    });
    expect(requests.at(-1)?.has('to')).toBe(false);
  });

  it('keeps the old chart, dimmed, until the new range arrives', async () => {
    setNow(sessionNow);
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.get('/api/portfolio/history', async ({ request }) => {
        if (new URL(request.url).searchParams.has('from')) await gate;
        return HttpResponse.json(buildHistory());
      }),
    );
    renderPortfolio();
    await revealChart();

    await userEvent.click(rangeButton('1M'));

    const chart = await screen.findByRole('img', { name: /Portfolio value history/ });
    expect(chart.parentElement).toHaveAttribute('aria-busy', 'true');
    release();
    await vi.waitFor(() => {
      expect(screen.getByRole('img', { name: /Portfolio value history/ }).parentElement).not.toHaveAttribute(
        'aria-busy',
      );
    });
  });
});

describe('History card: lines', () => {
  it('toggles Value, Invested and the benchmark from the legend', async () => {
    renderPortfolio();
    const card = await historyCard();
    await revealChart();
    const legend = within(card).getByRole('group', { name: 'Lines shown' });

    await userEvent.click(within(legend).getByRole('button', { name: 'Nifty 50 (same cash flows)' }));
    expect(drawnSeries().map((s) => s.name)).toEqual(['Value', 'Invested']);

    await userEvent.click(within(legend).getByRole('button', { name: 'Invested' }));
    expect(drawnSeries().map((s) => s.name)).toEqual(['Value']);
  });

  it('names the benchmark line after the index the history reports', async () => {
    captureHistory(buildHistory({ benchmark: buildHistoryBenchmark({ name: 'Nifty 100', symbol: 'NIFTY 100' }) }));
    renderPortfolio();
    await revealChart();

    expect(screen.getByRole('button', { name: 'Nifty 100 (same cash flows)' })).toBeInTheDocument();
  });

  it('shows the figures as a table in rupees', async () => {
    renderPortfolio();
    await revealChart();

    await userEvent.click(screen.getByRole('button', { name: 'Show as table' }));

    const table = screen.getByRole('table', { name: 'Portfolio value history, data table' });
    const newest = within(table).getAllByRole('row')[1];
    expect(newest).toHaveTextContent('9 Oct 2026');
    expect(newest).toHaveTextContent('₹2,03,400');
    expect(newest).toHaveTextContent('₹1,85,000');
    expect(newest).toHaveTextContent('₹1,96,000');
  });

  it('leaves the benchmark line out when the benchmark is unavailable', async () => {
    captureHistory(
      buildHistory({
        benchmark: buildUnavailableBenchmark(),
        points: sampleHistoryPoints().map((p) => ({ ...p, benchmarkValue: null, benchmarkNav: null })),
      }),
    );
    renderPortfolio();
    await revealChart();

    expect(drawnSeries().map((s) => s.name)).toEqual(['Value', 'Invested']);
  });
});

describe('History card: scope', () => {
  it('asks for the account and bucket of the scope bar', async () => {
    const bucket = buildBucket().id;
    const requests = captureHistory();
    renderPortfolio(`?account=${ids.main}&bucket=${bucket}`);
    await revealChart();

    expect(requests[0]?.get('accountId')).toBe(ids.main);
    expect(requests[0]?.get('bucketId')).toBe(bucket);
  });

  it('reads the history again when the scope changes', async () => {
    const requests = captureHistory();
    const { router } = renderPortfolio();
    await revealChart();
    expect(requests.at(-1)?.has('accountId')).toBe(false);

    await act(() => router.navigate(`/portfolio?account=${ids.main}`));

    await vi.waitFor(() => {
      expect(requests.at(-1)?.get('accountId')).toBe(ids.main);
    });
  });
});

describe('History card: coverage line', () => {
  const coverage = () => screen.findByRole('list', { name: 'History coverage' });

  it('says how many holdings the history covers', async () => {
    renderPortfolio();
    expect(await within(await coverage()).findByText('History covers 4 of 4 holdings')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Track my holdings' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Track the benchmark' })).not.toBeInTheDocument();
  });

  it('offers Track my holdings when some have no price history, for the scope, and tells the result', async () => {
    const calls: URLSearchParams[] = [];
    captureHistory(buildHistory({ coverage: { holdings: 43, covered: 41, unpriced: 2, lateStart: 0 } }));
    server.use(
      http.post('/api/portfolio/history/track-holdings', ({ request }) => {
        calls.push(new URL(request.url).searchParams);
        return HttpResponse.json(buildTrackResult({ added: 3, hint: 'Run Update data to build their history.' }));
      }),
    );
    renderPortfolio(`?account=${ids.main}`);

    expect(await within(await coverage()).findByText(/History covers 41 of 43 holdings/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Track my holdings' }));

    expect(
      await screen.findByText('Tracked 3 more holdings — run Update data to build their history'),
    ).toBeInTheDocument();
    expect(calls[0]?.get('accountId')).toBe(ids.main);
  });

  it('disables the button while the tracking runs, then reads the history and the totals again', async () => {
    let histories = 0;
    let summaries = 0;
    captureHistory(buildHistory({ coverage: { holdings: 4, covered: 3, unpriced: 1, lateStart: 0 } }));
    server.use(
      http.get('/api/portfolio/history', () => {
        histories += 1;
        return HttpResponse.json(buildHistory({ coverage: { holdings: 4, covered: 3, unpriced: 1, lateStart: 0 } }));
      }),
      http.get('/api/holdings/summary', () => {
        summaries += 1;
        return HttpResponse.json(buildSummary());
      }),
      http.post('/api/portfolio/history/track-holdings', async () => {
        await delay(100);
        return HttpResponse.json(buildTrackResult());
      }),
    );
    renderPortfolio();
    const button = await screen.findByRole('button', { name: 'Track my holdings' });
    const before = { histories, summaries };

    await userEvent.click(button);
    expect(button).toBeDisabled();

    await screen.findByText(/Tracked 3 more holdings/);
    await vi.waitFor(() => {
      expect(histories).toBeGreaterThan(before.histories);
      expect(summaries).toBeGreaterThan(before.summaries);
    });
    expect(screen.getByRole('button', { name: 'Track my holdings' })).toBeEnabled();
  });

  it('says what could not be tracked', async () => {
    captureHistory(buildHistory({ coverage: { holdings: 4, covered: 3, unpriced: 1, lateStart: 0 } }));
    server.use(
      http.post('/api/portfolio/history/track-holdings', () =>
        HttpResponse.json(
          buildTrackResult({ requested: 0, added: 0, alreadyTracked: 0, notInMaster: 1, hint: 'Nothing to do.' }),
        ),
      ),
    );
    renderPortfolio();

    await userEvent.click(await screen.findByRole('button', { name: 'Track my holdings' }));

    expect(
      await screen.findByText('No open holdings could be tracked; 1 holding is not in the instrument master'),
    ).toBeInTheDocument();
  });

  it('reports a failed tracking and leaves the button usable', async () => {
    captureHistory(buildHistory({ coverage: { holdings: 4, covered: 3, unpriced: 1, lateStart: 0 } }));
    server.use(
      http.post('/api/portfolio/history/track-holdings', () =>
        problemResponse({ title: 'Tracking is locked.', code: 'portfolio.conflict' }, 409),
      ),
    );
    renderPortfolio();

    await userEvent.click(await screen.findByRole('button', { name: 'Track my holdings' }));

    expect(await screen.findByText('Tracking is locked.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Track my holdings' })).toBeEnabled();
  });

  it('asks for the benchmark when it is not tracked, and tracks it', async () => {
    let called = 0;
    captureHistory(buildHistory({ benchmark: buildUnavailableBenchmark(), points: [] }));
    server.use(
      http.post('/api/portfolio/history/track-benchmark', () => {
        called += 1;
        return HttpResponse.json(
          buildTrackResult({ requested: 1, added: 1, hint: 'Run Update data to build its history.' }),
        );
      }),
    );
    renderPortfolio();

    const list = await coverage();
    expect(await within(list).findByText(/Benchmark needs Nifty 50 history/)).toBeInTheDocument();
    expect(within(list).getByText(/Nifty 50 is not tracked, so it has no stored prices\./)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Track the benchmark' }));

    expect(await screen.findByText('Tracking Nifty 50 — run Update data to build its history')).toBeInTheDocument();
    expect(called).toBe(1);
  });

  it('says it is tracked already when the benchmark only lacks bars', async () => {
    captureHistory(
      buildHistory({
        benchmark: buildUnavailableBenchmark({ tracked: true, reason: 'Nifty 50 has no stored prices yet.' }),
        points: [],
      }),
    );
    server.use(
      http.post('/api/portfolio/history/track-benchmark', () =>
        HttpResponse.json(buildTrackResult({ requested: 1, added: 0, alreadyTracked: 1, hint: 'Run Update data.' })),
      ),
    );
    renderPortfolio();

    await userEvent.click(await screen.findByRole('button', { name: 'Track the benchmark' }));

    expect(await screen.findByText('Nifty 50 is already tracked — run Update data')).toBeInTheDocument();
  });

  it('gives the reason without a button when the benchmark starts after the first cash flow', async () => {
    captureHistory(
      buildHistory({
        benchmark: buildUnavailableBenchmark({
          tracked: true,
          hasBars: true,
          reason: 'Nifty 50 history starts on 1 Jun 2025, after your first purchase.',
        }),
      }),
    );
    renderPortfolio();

    const list = await coverage();
    expect(await within(list).findByText(/starts on 1 Jun 2025, after your first purchase/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Track the benchmark' })).not.toBeInTheDocument();
  });

  it('says the history starts on the date that lots without a date of their own were given', async () => {
    server.use(summaryWith({ defaultDateLots: 3 }));
    renderPortfolio();

    expect(
      await within(await coverage()).findByText('3 lots use the default date, so the history starts on 1 Jan 2025'),
    ).toBeInTheDocument();
  });

  it('leaves that out when no lot uses the default date, and says "lot" for one', async () => {
    server.use(summaryWith({ defaultDateLots: 0 }));
    const { unmount } = renderPortfolio();
    await within(await coverage()).findByText('History covers 4 of 4 holdings');
    expect(screen.queryByText(/use the default date, so the history/)).not.toBeInTheDocument();
    unmount();

    server.use(summaryWith({ defaultDateLots: 1 }));
    renderPortfolio();
    expect(
      await screen.findByText('1 lot uses the default date, so the history starts on 1 Jan 2025'),
    ).toBeInTheDocument();
  });

  it("says when today's point uses the latest price", async () => {
    captureHistory(buildHistory({ lastPriceAt: '2026-10-13T10:35:00+05:30' }));
    renderPortfolio();

    expect(
      await within(await coverage()).findByText("Today's point uses the latest price (10:35 IST)"),
    ).toBeInTheDocument();
  });

  it("says the index is valued at its last close when today's live point has no index value", async () => {
    captureHistory(
      buildHistory({
        lastPriceAt: '2026-10-13T10:35:00+05:30',
        points: [
          ...sampleHistoryPoints(),
          buildHistoryPoint('2026-10-13', {
            value: 205_000,
            invested: 185_000,
            benchmarkValue: null,
            benchmarkNav: null,
          }),
        ],
      }),
    );
    renderPortfolio();
    await revealChart();

    expect(await within(await coverage()).findByText('Nifty 50 valued at the close of 9 Oct 2026')).toBeInTheDocument();
    // The benchmark line simply ends: the live day has no value for it (no gap-filling, no zero).
    expect(
      drawnSeries()
        .find((s) => s.id === 'benchmark')
        ?.data.at(-1),
    ).toBeNull();
  });

  it('says nothing about the index close while the benchmark is as recent as the portfolio', async () => {
    renderPortfolio();
    await within(await coverage()).findByText('History covers 4 of 4 holdings');
    expect(screen.queryByText(/valued at the close of/)).not.toBeInTheDocument();
  });

  it('notes the holdings whose history starts at their first stored price', async () => {
    captureHistory(buildHistory({ coverage: { holdings: 4, covered: 4, unpriced: 0, lateStart: 2 } }));
    renderPortfolio();

    expect(
      await within(await coverage()).findByText('2 holdings start at their first stored price'),
    ).toBeInTheDocument();
  });

  it('says "holding … its" for one', async () => {
    captureHistory(buildHistory({ coverage: { holdings: 4, covered: 4, unpriced: 0, lateStart: 1 } }));
    renderPortfolio();

    expect(await within(await coverage()).findByText('1 holding starts at its first stored price')).toBeInTheDocument();
  });
});

describe('History card: while a new range or scope loads', () => {
  const unpriced = { holdings: 4, covered: 3, unpriced: 1, lateStart: 0 };

  /** Answers at once without `from` / `accountId`; with either, only after `release()`. */
  function gated() {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.get('/api/portfolio/history', async ({ request }) => {
        const params = new URL(request.url).searchParams;
        if (params.has('from') || params.has('accountId')) await gate;
        return HttpResponse.json(buildHistory({ coverage: unpriced }));
      }),
    );
    return release;
  }

  it('dims the coverage line and disables its actions while another range loads, then enables them', async () => {
    setNow(sessionNow);
    const release = gated();
    renderPortfolio();
    const track = await screen.findByRole('button', { name: 'Track my holdings' });
    expect(track).toBeEnabled();

    await userEvent.click(rangeButton('1M'));

    await vi.waitFor(() => {
      expect(screen.getByRole('button', { name: 'Track my holdings' })).toBeDisabled();
    });
    expect(screen.getByRole('list', { name: 'History coverage' })).toHaveAttribute('aria-busy', 'true');

    release();
    await vi.waitFor(() => {
      expect(screen.getByRole('button', { name: 'Track my holdings' })).toBeEnabled();
    });
    expect(screen.getByRole('list', { name: 'History coverage' })).not.toHaveAttribute('aria-busy');
  });

  it('disables the Track buttons while the history of another scope loads', async () => {
    const release = gated();
    const { router } = renderPortfolio();
    await screen.findByRole('button', { name: 'Track my holdings' });

    await act(() => router.navigate(`/portfolio?account=${ids.main}`));

    await vi.waitFor(() => {
      expect(screen.getByRole('button', { name: 'Track my holdings' })).toBeDisabled();
    });
    release();
    await vi.waitFor(() => {
      expect(screen.getByRole('button', { name: 'Track my holdings' })).toBeEnabled();
    });
  });
});

describe('History card on a phone', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const skeletonHeight = async () => {
    const card = await historyCard();
    const status = await within(card).findByRole('status', { name: 'Loading chart' });
    return status.firstElementChild as HTMLElement;
  };

  it('draws a 240 px chart area; a wider screen gets 300 px', async () => {
    server.use(http.get('/api/portfolio/history', () => new Promise<never>(() => undefined)));
    const { unmount } = renderPortfolio();
    expect(await skeletonHeight()).toHaveStyle({ height: '300px' });
    unmount();

    stubPhoneWidth();
    renderPortfolio();
    expect(await skeletonHeight()).toHaveStyle({ height: '240px' });
  });

  it('lets the title and the range chips wrap, and keeps every chip', async () => {
    stubPhoneWidth();
    renderPortfolio();

    const card = await historyCard();
    const title = within(card).getByRole('heading', { name: 'History' });
    expect(title.parentElement).toHaveStyle({ flexWrap: 'wrap' });
    expect(within(screen.getByRole('group', { name: 'History range' })).getAllByRole('button')).toHaveLength(5);
  });

  it('keeps the legend and the table toggle under the chart', async () => {
    stubPhoneWidth();
    renderPortfolio();
    await revealChart();

    const legend = screen.getByRole('group', { name: 'Lines shown' });
    expect(legend.parentElement).toHaveStyle({ flexWrap: 'wrap' });
    expect(screen.getByRole('button', { name: 'Show as table' })).toBeInTheDocument();
  });
});
