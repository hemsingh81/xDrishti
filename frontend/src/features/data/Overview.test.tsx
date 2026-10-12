import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import {
  buildAggregatesOverview,
  buildBarsOverview,
  buildCsvOverview,
  buildCsvPhase,
  buildDataJob,
  buildDataOverview,
  buildDhanOverview,
  buildDhanPhase,
  buildEmptyDataOverview,
  ids,
} from '@/test/fixtures';
import { createDataFake } from '@/test/dataFake';
import { renderData } from './testing';

const day = 86_400_000;
const daysAgo = (n: number) => new Date(Date.now() - n * day).toISOString();
const strip = () => screen.findByRole('region', { name: 'Data freshness' });
const lastTick = async () => within(await strip()).getByRole('region', { name: 'Last tick' });
const pipeline = async () => screen.findByRole('region', { name: 'Pipeline' });
const step = async (name: RegExp) => within(await pipeline()).getByRole('listitem', { name });

describe('Data & import: the page', () => {
  it('has the title, the subtitle and the Update data button', async () => {
    renderData();

    expect(await screen.findByRole('heading', { level: 1, name: 'Data & import' })).toBeInTheDocument();
    expect(screen.getByText(/Load the CSV folder, fetch what is missing from Dhan/)).toBeInTheDocument();
    await strip();
    expect(screen.getByRole('button', { name: 'Update data' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'More update options' })).toBeEnabled();
  });

  it('shows a loading state, then the page', async () => {
    renderData();

    expect(screen.getByRole('progressbar', { name: 'Loading data overview' })).toBeInTheDocument();
    expect(await strip()).toBeInTheDocument();
  });

  it('says in words when the overview cannot be loaded, and loads it on Retry', async () => {
    const fake = createDataFake();
    fake.failures.overview = true;
    renderData({ fake });

    expect(await screen.findByText('The data service is not available.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Update data' })).toBeDisabled();

    fake.failures.overview = false;
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await strip()).toBeInTheDocument();
    expect(screen.queryByText('The data service is not available.')).not.toBeInTheDocument();
  });

  it('with no data yet, says where to put the CSV files and what to press', async () => {
    renderData({ fake: createDataFake({ overview: buildEmptyDataOverview(), coverage: [], jobs: [] }) });

    expect(
      await screen.findByText('No data yet — put your CSV files in data/Stocks_CSV and press Update data'),
    ).toBeInTheDocument();
    expect(within(await lastTick()).getByText('No bars stored yet')).toBeInTheDocument();
    expect(screen.queryByRole('grid', { name: 'Coverage' })).not.toBeInTheDocument();
    expect(within(await step(/CSV files/)).getByText('0 of 214 files loaded')).toBeInTheDocument();
    expect(within(await step(/CSV files/)).getByText('0 rows · 214 pending')).toBeInTheDocument();
    expect(screen.getByText('No updates have run yet.')).toBeInTheDocument();
  });
});

describe('Data & import: freshness strip', () => {
  it('highlights the last tick in India time as current, in words and with an icon', async () => {
    renderData({
      fake: createDataFake({
        overview: buildDataOverview({ bars: buildBarsOverview({ lastBar: daysAgo(3) }) }),
      }),
    });

    const tick = await lastTick();
    expect(within(tick).getByText('3 days ago')).toBeInTheDocument();
    expect(within(tick).getByText(/^Up to date · the 12 Oct 2026 session is in$/)).toBeInTheDocument();
    expect(within(tick).getByTestId('CheckCircleOutlinedIcon')).toBeInTheDocument();
    expect(within(tick).queryByText(/Behind/)).not.toBeInTheDocument();
  });

  it('shows the exact last bar in IST whatever the viewer’s zone', async () => {
    renderData();

    const tick = await lastTick();
    const time = within(tick).getByText('12 Oct 2026, 15:29 IST');
    expect(time).toHaveAttribute('datetime', '2026-10-12T15:29:00+05:30');
  });

  it('marks data that is behind with the warning text and icon, not by colour alone', async () => {
    renderData({
      fake: createDataFake({
        overview: buildDataOverview({
          bars: buildBarsOverview({
            lastBar: daysAgo(3),
            stale: true,
            daysBehind: 2,
            instrumentsBehind: 12,
            instrumentsEmpty: 3,
          }),
        }),
      }),
    });

    const tick = await lastTick();
    expect(within(tick).getByText('Behind by 2 trading days · expected the 12 Oct 2026 session')).toBeInTheDocument();
    expect(within(tick).getByTestId('WarningAmberOutlinedIcon')).toBeInTheDocument();
    expect(within(tick).getByText('3 days ago')).toBeInTheDocument();
    expect(within(tick).queryByText(/Up to date/)).not.toBeInTheDocument();
    expect(within(await strip()).getByText('12 behind · 3 without bars')).toBeInTheDocument();
  });

  it('does not say "Up to date" when the newest bar is current but instruments are behind', async () => {
    renderData({
      fake: createDataFake({
        overview: buildDataOverview({ bars: buildBarsOverview({ instrumentsBehind: 213 }) }),
      }),
    });

    const tick = await lastTick();
    expect(within(tick).getByText('Newest bar is current — 213 instruments behind')).toBeInTheDocument();
    expect(within(tick).getByTestId('WarningAmberOutlinedIcon')).toBeInTheDocument();
    expect(within(tick).queryByText(/Up to date/)).not.toBeInTheDocument();
    expect(within(await strip()).getByText('213 behind')).toBeInTheDocument();
  });

  it('says "1 instrument" in the singular when only one is behind', async () => {
    renderData({
      fake: createDataFake({
        overview: buildDataOverview({ bars: buildBarsOverview({ instrumentsBehind: 1 }) }),
      }),
    });

    expect(await screen.findByText('Newest bar is current — 1 instrument behind')).toBeInTheDocument();
  });

  it('says "1 trading day" in the singular', async () => {
    renderData({
      fake: createDataFake({
        overview: buildDataOverview({ bars: buildBarsOverview({ stale: true, daysBehind: 1 }) }),
      }),
    });

    expect(await screen.findByText(/^Behind by 1 trading day · /)).toBeInTheDocument();
  });

  it('shows the instruments with data, the total 1-minute bars (compact) and the covered period', async () => {
    renderData();

    const region = await strip();
    expect(within(region).getByText('Instruments with data').nextElementSibling).toHaveTextContent('214');
    expect(within(region).getByText('All up to date')).toBeInTheDocument();
    expect(within(region).getByText('1-minute bars').nextElementSibling).toHaveTextContent('4.1Cr');
    expect(within(region).getByText('4,12,34,567 in total')).toBeInTheDocument();
    expect(within(region).getByText('1 Apr 2024 – 12 Oct 2026')).toBeInTheDocument();
  });

  it('shows the connected Data account without a reconnect link', async () => {
    renderData();

    const region = await strip();
    expect(within(region).getByText('Dhan · Main')).toBeInTheDocument();
    expect(within(region).getByText('Connected')).toBeInTheDocument();
    expect(within(region).queryByRole('link', { name: 'Reconnect' })).not.toBeInTheDocument();
  });

  it('offers Reconnect, with the reason, when the Dhan step cannot run, and opens the account', async () => {
    const reason = 'The Dhan sign-in has expired — reconnect the Data account.';
    const { router } = renderData({
      fake: createDataFake({
        overview: buildDataOverview({
          dhan: buildDhanOverview({
            account: { id: ids.main, name: 'Dhan · Main', status: 'expired' },
            canFetch: false,
            reason,
            reasonCode: 'data.dhan_not_connected',
          }),
        }),
      }),
    });

    const region = await strip();
    expect(within(region).getByText('Sign-in expired')).toBeInTheDocument();
    expect(within(region).getByText(reason)).toBeInTheDocument();
    const link = within(region).getByRole('link', { name: 'Reconnect' });
    expect(link).toHaveAttribute('href', `/accounts?account=${ids.main}`);

    await userEvent.click(link);
    expect(router.state.location.pathname).toBe('/accounts');
    expect(router.state.location.search).toBe(`?account=${ids.main}`);
  });

  it('points to Accounts when no account has the Data role', async () => {
    renderData({
      fake: createDataFake({
        overview: buildDataOverview({
          dhan: buildDhanOverview({
            account: null,
            canFetch: false,
            reason: 'No account has the Data role.',
            reasonCode: 'data.dhan_not_connected',
          }),
        }),
      }),
    });

    const region = await strip();
    expect(within(region).getByText('No Data account')).toBeInTheDocument();
    expect(within(region).getByRole('link', { name: 'Open Accounts' })).toHaveAttribute('href', '/accounts');
    expect(within(region).queryByRole('link', { name: 'Reconnect' })).not.toBeInTheDocument();
  });
});

describe('Data & import: pipeline', () => {
  it('shows the three steps with their last results', async () => {
    renderData();

    const csv = await step(/CSV files/);
    expect(within(csv).getByText('214 of 214 files loaded')).toBeInTheDocument();
    expect(within(csv).getByText('4,12,34,567 rows · 0 pending')).toBeInTheDocument();
    expect(within(csv).getByText('214 files loaded · 4.1Cr rows')).toBeInTheDocument();
    expect(within(csv).getByText('39 repaired, 0 rejected')).toBeInTheDocument();
    expect(within(csv).getByText('Succeeded')).toBeInTheDocument();
    expect(within(csv).getByText(/^Last load/)).toHaveTextContent('Last load 12 Oct 2026, 09:10 IST');
    expect(within(csv).getByText(/^Last run/)).toHaveTextContent('Last run 12 Oct 2026, 09:10 IST');

    const dhan = await step(/Dhan Data API/);
    expect(within(dhan).getByText(/^Last fetch/)).toHaveTextContent('Last fetch 12 Oct 2026, 09:20 IST');
    expect(within(dhan).getByText('4,500 bars added', { selector: 'p' })).toBeInTheDocument();
    expect(within(dhan).getByText('214 instruments · 4,500 bars added')).toBeInTheDocument();

    const aggregates = await step(/Aggregates/);
    const chips = within(within(aggregates).getByRole('list', { name: 'Bars per timeframe' }));
    expect(chips.getAllByRole('listitem')).toHaveLength(8);
    expect(chips.getByText('5m · 82.3L')).toBeInTheDocument();
    expect(chips.getByText('1M · 5K')).toBeInTheDocument();
    expect(within(aggregates).getByText(/^Last built/)).toHaveTextContent('Last built 12 Oct 2026, 09:30 IST');
  });

  it('says that nothing has run when no job exists', async () => {
    renderData({
      fake: createDataFake({
        overview: buildDataOverview({
          dhan: buildDhanOverview({ lastFetchAt: null, lastFetchBars: null }),
          aggregates: buildAggregatesOverview({ timeframes: [], lastBuiltAt: null }),
          lastJobs: { csv: null, dhan: null, aggregate: null },
        }),
      }),
    });

    expect(within(await step(/Dhan Data API/)).getByText('Nothing fetched yet')).toBeInTheDocument();
    expect(within(await step(/Aggregates/)).getByText('No aggregates built yet')).toBeInTheDocument();
    expect(within(await pipeline()).getAllByText('Not run yet')).toHaveLength(3);
  });

  it('hints at the folder when it is missing', async () => {
    renderData({
      fake: createDataFake({
        overview: buildDataOverview({
          csv: buildCsvOverview({ folderExists: false, filesTotal: 0, filesLoaded: 0, rows: 0, lastLoadAt: null }),
        }),
      }),
    });

    const csv = await step(/CSV files/);
    expect(
      within(csv).getByText(
        'The CSV folder /data/csv was not found. Check that data/Stocks_CSV exists and is mounted into the API.',
      ),
    ).toBeInTheDocument();
  });

  it('counts the files that are still pending', async () => {
    renderData({
      fake: createDataFake({
        overview: buildDataOverview({
          csv: buildCsvOverview({ filesLoaded: 200, filesPending: 14, filesTotal: 214 }),
        }),
      }),
    });

    const csv = await step(/CSV files/);
    expect(within(csv).getByText('200 of 214 files loaded')).toBeInTheDocument();
    expect(within(csv).getByText('4,12,34,567 rows · 14 pending')).toBeInTheDocument();
  });

  it('lists the unknown symbols of the last load only on request', async () => {
    const csv = buildCsvPhase({
      unknownSymbols: ['NSE:OLDCO', 'NSE:ZZZ'],
      repaired: 39,
      rejected: 2,
      rejectionExamples: ['ABC_1m.csv line 5: price is zero'],
    });
    renderData({
      fake: createDataFake({
        overview: buildDataOverview({
          lastJobs: { csv: buildDataJob({ csv }), dhan: buildDataJob(), aggregate: buildDataJob() },
        }),
      }),
    });

    const card = await step(/CSV files/);
    expect(within(card).getByText('39 repaired, 2 rejected')).toBeInTheDocument();
    expect(within(card).queryByText('NSE:OLDCO')).not.toBeInTheDocument();

    const toggle = within(card).getByRole('button', { name: '2 unknown symbols' });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    await userEvent.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    expect(within(card).getByText('NSE:OLDCO')).toBeInTheDocument();
    expect(within(card).getByText('NSE:ZZZ')).toBeInTheDocument();

    await userEvent.click(within(card).getByRole('button', { name: 'Rejected rows (examples)' }));
    expect(within(card).getByText('ABC_1m.csv line 5: price is zero')).toBeInTheDocument();
  });

  it('shows the message and the code of a failed step in plain words', async () => {
    const dhan = buildDhanPhase({
      status: 'failed',
      message: 'Dhan refused the access token.',
      code: 'data.dhan_not_connected',
      bars: 0,
      inserted: 0,
    });
    renderData({
      fake: createDataFake({
        overview: buildDataOverview({
          lastJobs: {
            csv: buildDataJob(),
            dhan: buildDataJob({ status: 'failed', error: 'Dhan refused the access token.', dhan }),
            aggregate: buildDataJob(),
          },
        }),
      }),
    });

    const card = await step(/Dhan Data API/);
    expect(within(card).getByText('Failed')).toBeInTheDocument();
    expect(within(card).getByText(/Dhan refused the access token\./)).toBeInTheDocument();
    expect(within(card).getByText('Code data.dhan_not_connected')).toBeInTheDocument();
  });

  it('shows why a step was skipped, and uses the code when there is no message', async () => {
    const dhan = buildDhanPhase({ status: 'skipped', message: null, code: 'data.dhan_not_connected' });
    renderData({
      fake: createDataFake({
        overview: buildDataOverview({
          lastJobs: { csv: buildDataJob(), dhan: buildDataJob({ dhan }), aggregate: buildDataJob() },
        }),
      }),
    });

    const card = await step(/Dhan Data API/);
    expect(within(card).getByText('Skipped')).toBeInTheDocument();
    expect(
      within(card).getByText(/Dhan is not connected for the Data account\. Reconnect it in Accounts\./),
    ).toBeInTheDocument();
  });

  it('warns when overlapping Dhan bars differ from the stored ones', async () => {
    const dhan = buildDhanPhase({ overlapCompared: 1_000, overlapMismatched: 12, errors: ['NSE:ABC: timeout'] });
    renderData({
      fake: createDataFake({
        overview: buildDataOverview({
          lastJobs: { csv: buildDataJob(), dhan: buildDataJob({ dhan }), aggregate: buildDataJob() },
        }),
      }),
    });

    const card = await step(/Dhan Data API/);
    expect(within(card).getByText('12 of 1,000 overlapping bars differ from the stored ones')).toBeInTheDocument();
    await userEvent.click(within(card).getByRole('button', { name: '1 errors' }));
    expect(within(card).getByText('NSE:ABC: timeout')).toBeInTheDocument();
  });

  it('tells how many instruments are waiting to be aggregated', async () => {
    renderData({
      fake: createDataFake({
        overview: buildDataOverview({ aggregates: buildAggregatesOverview({ instrumentsPending: 7 }) }),
      }),
    });

    expect(
      within(await step(/Aggregates/)).getByText('7 instruments have new bars not aggregated yet'),
    ).toBeInTheDocument();
  });
});

describe('Data & import: recent runs', () => {
  it('lists the runs newest first with steps, result and counts, and the message of a failed run', async () => {
    const failed = buildDataJob({
      id: '00000000-0000-4000-8000-000000000899',
      status: 'failed',
      startedAt: '2026-10-11T09:00:00+05:30',
      steps: ['dhan'],
      error: 'Dhan refused the access token.',
      csv: buildCsvPhase({ status: 'notRequested' }),
      dhan: buildDhanPhase({ status: 'failed', message: 'Dhan refused the access token.' }),
      aggregate: buildDataJob().aggregate,
    });
    renderData({ fake: createDataFake({ jobs: [buildDataJob(), failed] }) });

    const list = await screen.findByRole('list', { name: 'Recent runs' });
    const [first, second] = within(list).getAllByRole('listitem');
    expect(first).toHaveTextContent('12 Oct 2026, 09:00 IST');
    expect(first).toHaveTextContent('All steps');
    expect(first).toHaveTextContent('Succeeded');
    expect(first).toHaveTextContent('214 files loaded · 4.1Cr rows');
    expect(first).toHaveTextContent('214 instruments · 4,500 bars added');
    expect(first).toHaveTextContent('1.2Cr bars written');
    expect(second).toHaveTextContent('11 Oct 2026, 09:00 IST');
    expect(second).toHaveTextContent('Dhan');
    expect(second).toHaveTextContent('Failed');
    expect(second).toHaveTextContent('Dhan refused the access token.');
  });

  it('says so when the runs cannot be loaded, and retries', async () => {
    const fake = createDataFake();
    fake.failures.jobs = true;
    renderData({ fake });

    expect(await screen.findByText('The data service is not available.')).toBeInTheDocument();
    fake.failures.jobs = false;
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByRole('list', { name: 'Recent runs' })).toBeInTheDocument();
  });
});
