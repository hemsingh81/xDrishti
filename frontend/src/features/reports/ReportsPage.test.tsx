import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { stubPhoneWidth } from '@/test/matchMedia';
import { pinClock } from '@/test/pinClock';
import {
  allFigures,
  buildGroup,
  buildRun,
  buildRunsView,
  buildEvidence,
  buildWhatWorks,
  nonValue,
  rankedWhatWorks,
  runKeyA,
  runKeyB,
} from '@/test/reportFixtures';
import { dimensionReasons, renderReports, renderReportsFromStart, reportServer } from '@/test/reportScreen';
import { problemResponse, server } from '@/test/server';

pinClock('2026-10-25T10:00:00Z');

const basketId = '00000000-0000-4000-8000-000000000401';
const markup = '<img src=x onerror=alert(1)>';

describe('Reports screen', () => {
  it('says there are no runs and shows every panel as empty, without a digit or a flat line', async () => {
    reportServer({ runs: buildRunsView([]) });
    const { container } = renderReports();
    expect(await screen.findByText(/No stored simulation runs yet/)).toBeInTheDocument();
    expect(screen.getByText('xd simulate')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Key figures' })).toHaveTextContent('No records yet');
    expect(container.querySelectorAll('[data-state="value"]')).toHaveLength(0);
    expect(container.querySelector('canvas')).toBeNull();
  });

  it('opens with the server headline and its counts, not page prose', async () => {
    reportServer({ whatWorks: buildWhatWorks({ headline: `Server says: ${markup}` }) });
    const { container } = renderReports();
    const card = await screen.findByRole('region', { name: 'What works' });
    expect(await within(card).findByText(`Server says: ${markup}`)).toBeInTheDocument();
    expect(card).toHaveTextContent('Cells looked at 24, largest n 28, trades required 45');
    expect(container.querySelector('img')).toBeNull();
    expect(within(card).getByRole('button', { name: 'See the full answer' })).toBeInTheDocument();
  });

  it('renders every free-text field as text', async () => {
    reportServer({
      runs: buildRunsView([buildRun({ strategyId: markup, universeLabel: markup, style: markup })]),
    });
    const { container } = renderReports();
    await screen.findByRole('region', { name: 'Data used' });
    await waitFor(() => {
      expect(container.querySelectorAll('img')).toHaveLength(0);
    });
    expect((await screen.findAllByText(new RegExp(markup.replace(/[()]/g, '.')))).length).toBeGreaterThan(0);
  });

  it('draws the Overview panels with their evidence', async () => {
    reportServer();
    renderReports();
    for (const name of [
      'Key figures',
      'Auto-insights',
      'Equity and drawdown',
      'Monthly total R',
      'Strategy by timeframe',
      'Expectancy by basket',
      'R distribution',
      'Win rate by weekday',
      'Data used',
    ]) {
      expect(await screen.findByRole('region', { name })).toBeInTheDocument();
    }
    const insights = await screen.findByRole('list', { name: 'Insights' });
    expect(within(insights).getAllByRole('listitem')).toHaveLength(8);
    expect(within(insights).getByText('Best strategy: orb at +0.21 R over 412 trades.')).toBeInTheDocument();
    expect(await screen.findByText(/Calibration: needs a champion model/)).toBeInTheDocument();
    const monthly = await screen.findByRole('list', { name: 'Monthly total R' });
    expect(within(monthly).getByText('+4.20 R')).toBeInTheDocument();
    expect(within(monthly).getByText('-2.10 R')).toBeInTheDocument();
    const weekdays = await screen.findByRole('list', { name: 'Win rate by weekday' });
    expect(
      within(weekdays)
        .getAllByRole('listitem')
        .map((li) => li.textContent.slice(0, 3)),
    ).toEqual(['Mon', 'Tue']);
    const basket = await screen.findByRole('region', { name: 'Expectancy by basket' });
    expect(await within(basket).findByText('MyChoice')).toBeInTheDocument();
    expect(basket).toHaveTextContent('A trade in several baskets appears under each; totals are not additive.');
    const grid = await screen.findByRole('grid', { name: 'Expectancy by strategy and timeframe' });
    expect(within(grid).getAllByRole('gridcell')).toHaveLength(2);
    expect(within(grid).getByRole('button', { name: /orb, 1h: insufficient data/ })).toBeInTheDocument();
    expect(screen.getAllByText(/Gross R \(before costs\)/).length).toBeGreaterThan(0);
  });

  it.each([
    ['insufficient', 'Insufficient data'],
    ['unavailable', 'Not available'],
    ['not_evaluable', 'Not evaluable'],
    ['empty', 'No records yet'],
  ] as const)('prints a %s figure as its word with no digit', async (state, word) => {
    reportServer({
      summary: { all: allFigures(nonValue(state, 'Reason text'), { n: 12 }), evidence: buildEvidence({ n: 12 }) },
    });
    renderReports();
    const kpi = await screen.findByRole('region', { name: 'Key figures' });
    await waitFor(() => {
      expect(within(kpi).getAllByText(word).length).toBeGreaterThanOrEqual(6);
    });
    expect(kpi).not.toHaveTextContent('+0.21');
    expect(kpi).not.toHaveTextContent('55.8%');
    expect(kpi.querySelectorAll('[data-state="value"]')).toHaveLength(1); // the trades count
  });

  it('shows the full What works answer from the server fields', async () => {
    reportServer({ whatWorks: rankedWhatWorks() });
    renderReports('?view=what-works');
    const ranked = await screen.findByRole('table', { name: 'Ranked cells' });
    expect(within(ranked).getAllByRole('row')).toHaveLength(4);
    expect(await within(ranked).findAllByText(/basket MyChoice · strategy orb · timeframe 15m/)).toHaveLength(3);
    const notRanked = screen.getByRole('table', { name: 'Cells not ranked' });
    expect(within(notRanked).getAllByRole('row')).toHaveLength(5);
    expect(within(notRanked).getByText('Only 14 trades; 45 are needed.')).toBeInTheDocument();
    expect(
      screen.getByText(
        /Looked at 7 cells, compared 7\. The best of 24 looks good by chance\. About 0\.4 positive lower bounds/,
      ),
    ).toBeInTheDocument();
    expect(screen.getByText('exit is left out: no run recorded it')).toBeInTheDocument();
  });

  it('reaches the filtered answer in at most 4 interactions from a cold route (3 to the answer)', async () => {
    const requests = reportServer();
    const user = userEvent.setup();
    let interactions = 0;
    const act = async (run: () => Promise<void>) => {
      interactions += 1;
      await run();
    };
    renderReportsFromStart();
    await act(() => user.click(screen.getByRole('link', { name: 'Reports' })));
    await act(async () => {
      await user.click(await screen.findByRole('combobox', { name: 'Baskets' }));
    });
    await act(() => user.click(screen.getByRole('option', { name: 'MyChoice' })));
    // The multi-select menu stays open (it hides the page from assistive technology); closing it is the fourth step.
    await act(() => user.keyboard('{Escape}'));
    await waitFor(() => {
      expect(
        requests.some((url) => url.pathname.endsWith('/what-works') && url.searchParams.get('basket') === basketId),
      ).toBe(true);
    });
    expect(await screen.findByRole('button', { name: /Remove filter Baskets MyChoice/ })).toBeInTheDocument();
    expect(interactions).toBeLessThanOrEqual(4);
  });

  it('opens from a link with view and basket, sends the filter on every request, and Back works', async () => {
    const requests = reportServer({ whatWorks: rankedWhatWorks() });
    const { router } = renderReports(`?view=what-works&basket=${basketId}&side=BUY&bogus=1&from=not-a-date`);
    await screen.findByRole('table', { name: 'Ranked cells' });
    const reportCalls = requests.filter(
      (url) =>
        !url.pathname.endsWith('/runs') &&
        url.searchParams.get('lens') !== 'regime' &&
        url.searchParams.get('lens') !== 'account',
    );
    expect(reportCalls.length).toBeGreaterThan(0);
    for (const url of reportCalls) {
      expect(url.searchParams.get('basket')).toBe(basketId);
      expect(url.searchParams.get('side')).toBe('BUY');
      expect(url.searchParams.has('from')).toBe(false);
    }
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'Overview' }));
    expect(router.state.location.search).not.toContain('view=');
    await router.navigate(-1);
    expect(await screen.findByRole('table', { name: 'Ranked cells' })).toBeInTheDocument();
  });

  it('measures date presets back from the end of the data, not from today', async () => {
    reportServer();
    const { router } = renderReports();
    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: '1M' }));
    expect(router.state.location.search).toContain('from=2026-03-31');
    expect(screen.getByRole('button', { name: '1M' })).toHaveAttribute('aria-pressed', 'true');
    await user.click(screen.getByRole('button', { name: 'All' }));
    expect(router.state.location.search).not.toContain('from=');
  });

  it('lists every run, used or left out, and sets runs= from the picker', async () => {
    reportServer({
      runs: buildRunsView([
        buildRun(),
        buildRun({
          runKey: runKeyB,
          strategyId: 'vwap',
          inDefaultSet: false,
          defaultSetNote: 'An older run of the same strategy.',
        }),
      ]),
    });
    const { router } = renderReports();
    const user = userEvent.setup();
    const table = await screen.findByRole('table', { name: 'Runs behind the numbers' });
    expect(await within(table).findByText('Used')).toBeInTheDocument();
    expect(within(table).getByText(/An older run of the same strategy/)).toBeInTheDocument();
    await user.click(within(table).getByRole('checkbox', { name: /Use run vwap/ }));
    expect(router.state.location.search).toContain(`runs=${runKeyA}%2C${runKeyB}`);
    await user.click(within(table).getByRole('checkbox', { name: /Use run vwap/ }));
    expect(router.state.location.search).not.toContain('runs=');
  });

  it('shows the server message for a run conflict and a way back to the default run set', async () => {
    reportServer();
    server.use(
      http.get('/api/reports/summary', () =>
        problemResponse({ title: 'Runs a and b would count the same trades twice.', code: 'reports.run_overlap' }, 409),
      ),
    );
    const { router } = renderReports(`?runs=${runKeyA}`);
    expect(await screen.findByText('Runs a and b would count the same trades twice.')).toBeInTheDocument();
    await userEvent.setup().click(screen.getAllByRole('button', { name: 'Use the default run set' })[0]!);
    expect(router.state.location.search).not.toContain('runs=');
  });

  it('shows the too-large refusal with the same action', async () => {
    reportServer();
    server.use(
      http.get('/api/reports/insights', () =>
        problemResponse({ title: 'The run holds too many trades to read.', code: 'reports.too_large' }, 422),
      ),
    );
    renderReports();
    expect(await screen.findByText('The run holds too many trades to read.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Use the default run set' })).toBeInTheDocument();
  });

  it('stays gross when net is asked and not confirmed, saying why', async () => {
    const requests = reportServer();
    renderReports('?basis=net');
    expect(
      await screen.findByText(/The charge sheet is not confirmed, so every report is gross\. The figures stay gross\./),
    ).toBeInTheDocument();
    await screen.findByRole('region', { name: 'Key figures' });
    expect(requests.filter((url) => url.searchParams.has('basis'))).toHaveLength(0);
  });

  it('keeps regime and account disabled with the server reason', async () => {
    reportServer();
    renderReports();
    const regime = await screen.findByRole('textbox', { name: 'Regime' });
    expect(regime).toBeDisabled();
    expect(await screen.findByText(dimensionReasons.regime)).toBeInTheDocument();
    expect(await screen.findByText(dimensionReasons.account)).toBeInTheDocument();
  });

  it('collapses the filter bar below md, keeping the active filters as chips', async () => {
    stubPhoneWidth();
    reportServer();
    renderReports('?side=SELL');
    const button = await screen.findByRole('button', { name: 'Filters (1)' });
    expect(screen.queryByRole('combobox', { name: 'Baskets' })).toBeNull();
    expect(screen.getByRole('button', { name: 'Remove filter Side SELL' })).toBeInTheDocument();
    await userEvent.setup().click(button);
    expect(await screen.findByRole('combobox', { name: 'Baskets' })).toBeInTheDocument();
  });

  it('lists unbuilt reports with their slice and reason, and no chart', async () => {
    reportServer();
    renderReports();
    const nav = await screen.findByRole('navigation', { name: 'Report catalogue' });
    expect(within(nav).getAllByText(/Planned, P8-10: Not built yet\./).length).toBeGreaterThan(0);
    expect(within(nav).getByRole('button', { name: 'Basket comparison' })).toBeInTheDocument();
    expect(within(nav).getByRole('button', { name: 'Pivot builder' })).toBeInTheDocument();
    expect(within(nav).getByRole('button', { name: 'Overview' })).toHaveAttribute('aria-current', 'page');
  });

  it('keeps showing a panel that fails without blanking the page', async () => {
    reportServer();
    server.use(http.get('/api/reports/equity', () => HttpResponse.json({ title: 'Equity broke.' }, { status: 500 })));
    renderReports();
    expect(await screen.findByRole('region', { name: 'Equity and drawdown' })).toBeInTheDocument();
    expect(await screen.findByRole('region', { name: 'Key figures' })).toBeInTheDocument();
    await waitFor(() => {
      expect(
        within(screen.getByRole('region', { name: 'Equity and drawdown' })).getByRole('alert'),
      ).toBeInTheDocument();
    });
    expect(await screen.findByRole('list', { name: 'Insights' })).toBeInTheDocument();
  });

  it('keeps a figure that is a value in the gain colour only through the kit', async () => {
    reportServer({
      summary: { all: buildGroup({ expectancyR: nonValue('insufficient') }), evidence: buildEvidence() },
    });
    renderReports();
    const kpi = await screen.findByRole('region', { name: 'Key figures' });
    await waitFor(() => {
      expect(within(kpi).getByText(/n 412 of at least 30/)).toBeInTheDocument();
    });
  });
});
