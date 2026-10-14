import { renderHook, screen, waitFor, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import type { ReactNode } from 'react';
import { buildReturns, buildSummary } from '@/test/fixtures';
import { setNow } from '@/test/clock';
import {
  benchmarkInstrumentId,
  buildBenchmarkView,
  buildDrawdown,
  buildHistory,
  buildUnavailableBenchmark,
} from '@/test/historyFixtures';
import { problemResponse, server } from '@/test/server';
import { useSetBenchmark } from './api/historyMutations';
import { benchmarkQuery } from './api/historyQueries';
import { captureHistory, renderPortfolio } from './testing';
import type { HoldingsSummary } from '@/shared/api';
import { useQuery } from '@tanstack/react-query';
import { pendingResponse } from '@/test/gate';

const summaryWith = (overrides: Partial<HoldingsSummary>) => {
  server.use(http.get('/api/holdings/summary', () => HttpResponse.json(buildSummary(overrides))));
};

/** The tile with this label: its card. */
const tile = async (label: string | RegExp) => {
  const heading = await screen.findByText(label, { selector: 'p' });
  const card = heading.closest('.MuiCard-root');
  if (!card) throw new Error(`No tile ${String(label)}`);
  return within(card as HTMLElement);
};

describe('Benchmark and drawdown tiles', () => {
  it('shows the benchmark XIRR, alpha in percentage points with sign and arrow, and the max drawdown', async () => {
    renderPortfolio();

    const benchmark = await tile('Benchmark XIRR');
    expect(await benchmark.findByText('+9.10%')).toHaveTextContent('▲+9.10%');
    expect(benchmark.getByText('Nifty 50, same cash flows')).toBeInTheDocument();

    const alpha = await tile('Alpha');
    expect(alpha.getByText('+2.20 pp')).toHaveTextContent('▲+2.20 pp');
    expect(
      alpha.getByText('XIRR minus benchmark XIRR · price return, before dividends and charges'),
    ).toBeInTheDocument();

    const drawdown = await tile('Max drawdown (All)');
    expect(await drawdown.findByText('−6.25%')).toHaveTextContent('▼−6.25%');
    expect(drawdown.getByText('2 Jun → 1 Aug')).toBeInTheDocument();
  });

  it('shows a negative alpha with a minus sign and a down arrow', async () => {
    summaryWith({ benchmarkXirr: 12.4, alpha: -1.1 });
    renderPortfolio();

    expect((await tile('Alpha')).getByText('−1.10 pp')).toHaveTextContent('▼−1.10 pp');
  });

  it('says against which close alpha was worked out when the portfolio is at live prices', async () => {
    summaryWith({ alphaBasis: 'live_vs_last_close', benchmarkAsOf: '2026-10-12' });
    renderPortfolio();

    expect(
      (await tile('Alpha')).getByText(
        'Live prices vs Nifty 50 close of 12 Oct · price return, before dividends and charges',
      ),
    ).toBeInTheDocument();
  });

  it('explains in a tooltip that the live alpha compares the portfolio now with the index close', async () => {
    summaryWith({ alphaBasis: 'live_vs_last_close', benchmarkAsOf: '2026-10-12' });
    renderPortfolio();

    const alpha = await tile('Alpha');
    expect(
      await alpha.findByRole('img', {
        name: /Portfolio at live prices against Nifty 50 at its last close \(12 Oct 2026\)/,
      }),
    ).toHaveTextContent('+2.20 pp');
  });

  it('keeps ten tiles in the totals', async () => {
    renderPortfolio();
    await tile('Alpha');
    const labels = [
      'Invested',
      'Current value',
      'Day change',
      'Unrealised P&L',
      'Realised (est.)',
      'XIRR',
      'Benchmark XIRR',
      'Alpha',
      'Holdings',
    ];
    for (const label of labels) expect(screen.getByText(label, { selector: 'p' })).toBeInTheDocument();
    expect(screen.getByText(/^Max drawdown \(/, { selector: 'p' })).toBeInTheDocument();
  });

  it('gives the dates of the fall, and the benchmark fall, in a tooltip that opens from the keyboard', async () => {
    renderPortfolio();
    const drawdown = await tile('Max drawdown (All)');
    const figure = await drawdown.findByRole('img', {
      name: /−6\.25%\. The portfolio's unit value fell 6\.25% from its peak on 2 Jun 2025/,
    });
    expect(figure).toHaveAccessibleName(expect.stringContaining('Nifty 50 fell 1.85% over the same range.'));
    // The range as the server returned it, not only the chip.
    expect(figure).toHaveAccessibleName(expect.stringContaining('(range All: 1 Jan 2025 – 9 Oct 2026;'));

    for (let presses = 0; presses < 40 && document.activeElement !== figure; presses += 1) await userEvent.tab();

    expect(figure).toHaveFocus();
    expect(await screen.findByRole('tooltip')).toHaveTextContent(/to its low on 1 Aug 2025/);
  });

  it('labels the drawdown with the range and reads it for that range', async () => {
    setNow('2026-10-13T05:05:00Z');
    const requests = captureHistory((params) =>
      buildHistory({ maxDrawdown: buildDrawdown({ pct: params.has('from') ? 3.1 : 6.25 }) }),
    );
    renderPortfolio();
    await (await tile('Max drawdown (All)')).findByText('−6.25%');

    await userEvent.click(screen.getByRole('button', { name: '1Y' }));

    const drawdown = await tile('Max drawdown (1Y)');
    expect(await drawdown.findByText('−3.10%')).toBeInTheDocument();
    expect(requests.at(-1)?.get('from')).toBe('2025-10-13');
    expect(screen.queryByText('Max drawdown (All)')).not.toBeInTheDocument();
  });

  it('shows the drawdown as pending under the new label until the data of the new range arrives', async () => {
    setNow('2026-10-13T05:05:00Z');
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.get('/api/portfolio/history', async ({ request }) => {
        const asked = new URL(request.url).searchParams.has('from');
        if (asked) await gate;
        return HttpResponse.json(buildHistory({ maxDrawdown: buildDrawdown({ pct: asked ? 3.1 : 6.25 }) }));
      }),
    );
    renderPortfolio();
    await (await tile('Max drawdown (All)')).findByText('−6.25%');

    await userEvent.click(screen.getByRole('button', { name: '1Y' }));

    const pending = await tile('Max drawdown (1Y)');
    expect(pending.getByText('…')).toBeInTheDocument();
    expect(pending.getByText('Loading')).toBeInTheDocument();
    expect(pending.queryByText('−6.25%')).not.toBeInTheDocument();

    release();
    expect(await pending.findByText('−3.10%')).toBeInTheDocument();
  });

  it('shows 0.00% and says there was no fall when the value never fell', async () => {
    captureHistory(buildHistory({ maxDrawdown: { pct: 0, from: null, to: null } }));
    renderPortfolio();

    const drawdown = await tile('Max drawdown (All)');
    expect(await drawdown.findByText('0.00%')).toBeInTheDocument();
    expect(drawdown.getByText('No fall in this range')).toBeInTheDocument();
  });

  it('shows a dash with the reason when there is no history for a drawdown', async () => {
    captureHistory(buildHistory({ points: [], maxDrawdown: null, benchmarkMaxDrawdown: null }));
    renderPortfolio();

    const drawdown = await tile('Max drawdown (All)');
    expect(await drawdown.findByRole('img', { name: /no value history in this range yet/ })).toHaveTextContent('—');
    expect(drawdown.getByText('No history yet')).toBeInTheDocument();
  });

  it('shows a dash with the reason when the history could not be loaded, and keeps the other tiles', async () => {
    server.use(
      http.get('/api/portfolio/history', () =>
        problemResponse({ title: 'History failed.', code: 'portfolio.invalid' }, 500),
      ),
    );
    renderPortfolio();

    const drawdown = await tile('Max drawdown (All)');
    expect(await drawdown.findByRole('img', { name: /history could not be loaded/ })).toHaveTextContent('—');
    expect(await screen.findByText('₹1,85,000')).toBeInTheDocument();
    expect((await tile('Benchmark XIRR')).getByText('+9.10%')).toBeInTheDocument();
  });

  it('waits for the history before showing the drawdown', async () => {
    server.use(http.get('/api/portfolio/history', () => pendingResponse()));
    renderPortfolio();

    const drawdown = await tile('Max drawdown (All)');
    expect(drawdown.getByText('…')).toBeInTheDocument();
    expect(drawdown.getByText('Loading')).toBeInTheDocument();
  });

  describe('when the benchmark is not available', () => {
    it('shows dashes and the reason the history gives for an untracked index', async () => {
      summaryWith({ benchmarkXirr: null, alpha: null, benchmarkState: 'unavailable' });
      captureHistory(buildHistory({ benchmark: buildUnavailableBenchmark(), points: [], maxDrawdown: null }));
      renderPortfolio();

      const benchmark = await tile('Benchmark XIRR');
      await waitFor(() => {
        expect(
          benchmark.getByRole('img', { name: 'Nifty 50 is not tracked, so it has no stored prices.' }),
        ).toHaveTextContent('—');
      });
      expect(benchmark.getByText('No Nifty 50 history')).toBeInTheDocument();

      const alpha = await tile('Alpha');
      expect(
        alpha.getByRole('img', { name: /^Alpha needs the benchmark XIRR\. Nifty 50 is not tracked/ }),
      ).toHaveTextContent('—');
      expect(alpha.getByText('No Nifty 50 history')).toBeInTheDocument();
    });

    it('explains an unavailable benchmark without the history, in general words', async () => {
      summaryWith({ benchmarkXirr: null, alpha: null, benchmarkState: 'unavailable' });
      server.use(
        http.get('/api/portfolio/history', () => problemResponse({ title: 'x', code: 'portfolio.invalid' }, 500)),
      );
      renderPortfolio();

      const benchmark = await tile('Benchmark XIRR');
      expect(
        await benchmark.findByRole('img', { name: /has no price history from the first purchase/ }),
      ).toBeInTheDocument();
    });

    it('shows the XIRR reason for a benchmark that needs more time', async () => {
      summaryWith({ benchmarkXirr: null, alpha: null, benchmarkState: 'insufficient' });
      renderPortfolio();

      const benchmark = await tile('Benchmark XIRR');
      expect(
        await benchmark.findByRole('img', { name: /first purchase at least 30 days before today/ }),
      ).toHaveTextContent('—');
      expect(benchmark.getByText('Needs 30+ days of history')).toBeInTheDocument();
    });

    it('shows n/m when the benchmark rate is beyond ±1,000 %', async () => {
      summaryWith({ benchmarkXirr: null, alpha: null, benchmarkState: 'n_m' });
      renderPortfolio();

      const benchmark = await tile('Benchmark XIRR');
      expect(await benchmark.findByRole('img', { name: /beyond ±1,000 %/ })).toHaveTextContent('n/m');
    });

    it('shows no alpha when the portfolio has no XIRR of its own, and says so', async () => {
      summaryWith({
        alpha: null,
        returns: buildReturns({ xirr: null, xirrState: 'insufficient' }),
      });
      renderPortfolio();

      const alpha = await tile('Alpha');
      expect(await alpha.findByRole('img', { name: /^Alpha needs your XIRR\./ })).toHaveTextContent('—');
    });

    it('shows nothing of the benchmark in the legend', async () => {
      summaryWith({ benchmarkXirr: null, alpha: null, benchmarkState: 'unavailable' });
      captureHistory(buildHistory({ benchmark: buildUnavailableBenchmark(), points: [] }));
      renderPortfolio();

      await tile('Alpha');
      expect(screen.queryByRole('button', { name: /same cash flows/ })).not.toBeInTheDocument();
    });
  });
});

describe('the benchmark setting', () => {
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      {children}
    </QueryClientProvider>
  );

  it('reads the benchmark in force', async () => {
    const { result } = renderHook(() => useQuery(benchmarkQuery), { wrapper });
    await waitFor(() => {
      expect(result.current.data).toEqual(buildBenchmarkView());
    });
  });

  it('chooses another index by id', async () => {
    let body: unknown;
    server.use(
      http.put('/api/portfolio/benchmark', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(buildBenchmarkView({ isDefault: false }));
      }),
    );
    const { result } = renderHook(() => useSetBenchmark(), { wrapper });

    result.current.mutate(benchmarkInstrumentId);

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true);
    });
    expect(body).toEqual({ instrumentId: benchmarkInstrumentId });
  });
});
