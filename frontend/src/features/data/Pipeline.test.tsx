import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { appliedCss } from '@/test/css';
import {
  buildAggregatesOverview,
  buildCsvOverview,
  buildCsvPhase,
  buildDataJob,
  buildDataOverview,
  buildDhanOverview,
  buildDhanPhase,
} from '@/test/fixtures';
import { createDataFake } from '@/test/dataFake';
import { openStep, ready, renderData } from './testing';

// Poll fast so a running job moves on within the test instead of after the real 2 s.
vi.mock('./lib/polling', () => ({ dataPollMs: 25 }));

const pipeline = async () => {
  await ready();
  return screen.findByRole('region', { name: 'Pipeline' });
};
const chip = async (name: RegExp) => within(await pipeline()).getByRole('listitem', { name });
const withLast = (
  jobs: Partial<ReturnType<typeof buildDataOverview>['lastJobs']>,
  rest: Parameters<typeof buildDataOverview>[0] = {},
) =>
  createDataFake({
    overview: buildDataOverview({
      lastJobs: { csv: buildDataJob(), dhan: buildDataJob(), aggregate: buildDataJob(), ...jobs },
      ...rest,
    }),
  });

describe('Pipeline strip: the chips', () => {
  it('shows the three steps in one row with their key figures, outcome and last run', async () => {
    renderData();

    const steps = within(await pipeline()).getByRole('list', { name: 'Pipeline steps' });
    expect(within(steps).getAllByRole('listitem')).toHaveLength(3);
    const csv = await chip(/CSV files/);
    expect(csv).toHaveTextContent('1. CSV files');
    expect(within(csv).getByText('214 of 214 files')).toBeInTheDocument();
    expect(within(csv).getByText('Succeeded')).toBeInTheDocument();
    expect(within(csv).getByText('12 Oct 09:10')).toHaveAttribute('datetime', '2026-10-12T09:10:00+05:30');
    const dhan = await chip(/Dhan Data API/);
    expect(within(dhan).getByText('4,500 bars added · No open gaps')).toBeInTheDocument();
    expect(within(dhan).getByText('12 Oct 09:20')).toBeInTheDocument();
    const aggregates = await chip(/Aggregates/);
    expect(within(aggregates).getByText('8 timeframes')).toBeInTheDocument();
    expect(within(aggregates).getByText('12 Oct 09:30')).toBeInTheDocument();
    expect(within(csv).queryByText(/pending/)).not.toBeInTheDocument();
  });

  it('has no link to the Data quality screen (it is in the menu)', async () => {
    renderData();

    expect(within(await pipeline()).queryByRole('link', { name: 'Data quality' })).not.toBeInTheDocument();
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

    expect(within(await chip(/Dhan Data API/)).getByText(/^Nothing fetched yet/)).toBeInTheDocument();
    expect(within(await chip(/Aggregates/)).getByText('No aggregates built yet')).toBeInTheDocument();
    expect(within(await pipeline()).getAllByText('Not run yet')).toHaveLength(3);
  });

  it('counts the files that are still pending on the CSV chip, in words', async () => {
    renderData({
      fake: createDataFake({
        overview: buildDataOverview({ csv: buildCsvOverview({ filesLoaded: 200, filesPending: 14, filesTotal: 214 }) }),
      }),
    });

    const csv = await chip(/CSV files/);
    expect(within(csv).getByText('200 of 214 files')).toBeInTheDocument();
    expect(within(csv).getByText('14 pending')).toBeInTheDocument();
  });

  it('says on the chip when the CSV folder is missing', async () => {
    renderData({
      fake: createDataFake({
        overview: buildDataOverview({
          csv: buildCsvOverview({ folderExists: false, filesTotal: 0, filesLoaded: 0, rows: 0, lastLoadAt: null }),
        }),
      }),
    });

    expect(within(await chip(/CSV files/)).getByText('CSV folder not found')).toBeInTheDocument();
  });

  it('tells how many instruments are waiting to be aggregated', async () => {
    renderData({
      fake: createDataFake({
        overview: buildDataOverview({ aggregates: buildAggregatesOverview({ instrumentsPending: 7 }) }),
      }),
    });

    expect(within(await chip(/Aggregates/)).getByText('7 waiting')).toBeInTheDocument();
  });

  it('shows the reason on the chip of a failed or skipped step, with its icon and word', async () => {
    const failed = buildDhanPhase({
      status: 'failed',
      message: 'Dhan refused the access token.',
      code: 'data.dhan_not_connected',
    });
    const skipped = buildDataJob().aggregate;
    renderData({
      fake: withLast({
        dhan: buildDataJob({ status: 'failed', dhan: failed }),
        aggregate: buildDataJob({ aggregate: { ...skipped, status: 'skipped', message: 'An earlier step failed.' } }),
      }),
    });

    const dhan = await chip(/Dhan Data API/);
    expect(within(dhan).getByText('Failed')).toBeInTheDocument();
    expect(within(dhan).getByTestId('ErrorOutlineOutlinedIcon')).toBeInTheDocument();
    expect(within(dhan).getByText('Dhan refused the access token.')).toBeInTheDocument();
    const aggregates = await chip(/Aggregates/);
    expect(within(aggregates).getByText('Skipped')).toBeInTheDocument();
    expect(within(aggregates).getByTestId('RemoveCircleOutlineOutlinedIcon')).toBeInTheDocument();
    expect(within(aggregates).getByText('An earlier step failed.')).toBeInTheDocument();
  });

  it('stacks as a list on a phone and goes into three columns from md', async () => {
    renderData();
    const steps = within(await pipeline()).getByRole('list', { name: 'Pipeline steps' });

    const css = appliedCss(steps);
    expect(css.base).toContain('display: grid');
    const responsive = css.responsive.map((r) => `${r.query} ${r.css}`).join(' ');
    expect(responsive).toMatch(/min-width:0px\) .*grid-template-columns: 1fr/);
    expect(responsive).toMatch(/min-width:900px\) .*grid-template-columns: repeat\(3, minmax\(0, 1fr\)\)/);
  });
});

describe('Pipeline strip: while a job runs', () => {
  it('shows a determinate progress bar and the phase message on the running chip, and waiting on the others', async () => {
    const fake = createDataFake({ autoAdvance: false, running: { steps: ['csv', 'dhan', 'aggregate'] } });
    renderData({ fake });

    const csv = await chip(/CSV files/);
    const bar = await within(csv).findByRole('progressbar', { name: 'CSV files progress' });
    expect(bar).toHaveAttribute('aria-valuenow', '0');
    expect(within(csv).getByText('Running')).toBeInTheDocument();
    expect(within(await chip(/Dhan Data API/)).getByText('Waiting for the earlier steps')).toBeInTheDocument();
    expect(within(await chip(/Aggregates/)).getByText('Waiting for the earlier steps')).toBeInTheDocument();
    // The phase is announced politely, once per phase, from a live region.
    expect(screen.getByRole('status', { name: 'Update progress' })).toHaveTextContent(
      /^Update in progress, loading the CSV files\. Started /,
    );

    fake.advance();
    expect(await within(csv).findByText('107 of 214 files')).toBeInTheDocument();
    expect(within(csv).getByText('Loading RELIANCE_1m.csv.gz')).toBeInTheDocument();
    expect(within(csv).getByRole('progressbar', { name: 'CSV files progress' })).toHaveAttribute('aria-valuenow', '50');
  });

  it('keeps the live region on the page when nothing runs, empty', async () => {
    renderData();
    await pipeline();

    expect(screen.getByRole('status', { name: 'Update progress' })).toBeEmptyDOMElement();
  });

  it('still opens the details of a running step', async () => {
    renderData({ fake: createDataFake({ autoAdvance: false, running: { steps: ['csv', 'dhan', 'aggregate'] } }) });
    await pipeline();

    const details = await openStep('CSV files');

    expect(within(details).getByText('Running')).toBeInTheDocument();
    expect(within(details).getByRole('button', { name: /^Review files/ })).toBeInTheDocument();
  });
});

describe('Pipeline strip: the details popovers', () => {
  it('opens a labelled popover from the chip and closes it with Escape', async () => {
    renderData();
    await pipeline();
    const button = within(await chip(/CSV files/)).getByRole('button');
    expect(button).toHaveAttribute('aria-haspopup', 'dialog');
    expect(button).toHaveAttribute('aria-expanded', 'false');

    const details = await openStep('CSV files');

    expect(details).toHaveAccessibleName('CSV files details');
    expect(button).toHaveAttribute('aria-expanded', 'true');
    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('dialog', { name: 'CSV files details' })).not.toBeInTheDocument();
    expect(button).toHaveFocus();
  });

  it('shows the CSV step in full: counts, last load, last run, repaired and rejected', async () => {
    renderData();

    const csv = await openStep('CSV files');
    expect(within(csv).getByText('214 of 214 files loaded')).toBeInTheDocument();
    expect(within(csv).getByText('4,12,34,567 rows · 0 pending')).toBeInTheDocument();
    expect(within(csv).getByText('214 files loaded · 4.1Cr rows')).toBeInTheDocument();
    expect(within(csv).getByText('39 repaired, 0 rejected')).toBeInTheDocument();
    expect(within(csv).getByText('Succeeded')).toBeInTheDocument();
    expect(within(csv).getByText(/^Last load/)).toHaveTextContent('Last load 12 Oct 2026, 09:10 IST');
    expect(within(csv).getByText(/^Last run/)).toHaveTextContent('Last run 12 Oct 2026, 09:10 IST');
    expect(within(csv).getByRole('button', { name: 'Review files' })).toBeInTheDocument();
  });

  it('says in the popover that the CSV folder is missing and where it should be', async () => {
    renderData({
      fake: createDataFake({
        overview: buildDataOverview({
          csv: buildCsvOverview({ folderExists: false, filesTotal: 0, filesLoaded: 0, rows: 0, lastLoadAt: null }),
        }),
      }),
    });

    const csv = await openStep('CSV files');
    expect(
      within(csv).getByText(
        'The CSV folder /data/csv was not found. Check that data/Stocks_CSV exists and is mounted into the API.',
      ),
    ).toBeInTheDocument();
  });

  it('lists the unknown symbols and the rejected rows of the last load only on request', async () => {
    const csv = buildCsvPhase({
      unknownSymbols: ['NSE:OLDCO', 'NSE:ZZZ'],
      repaired: 39,
      rejected: 2,
      rejectionExamples: ['ABC_1m.csv line 5: price is zero'],
    });
    renderData({ fake: withLast({ csv: buildDataJob({ csv }) }) });

    const details = await openStep('CSV files');
    expect(within(details).getByText('39 repaired, 2 rejected')).toBeInTheDocument();
    expect(within(details).queryByText('NSE:OLDCO')).not.toBeInTheDocument();

    const toggle = within(details).getByRole('button', { name: '2 unknown symbols' });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    await userEvent.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    expect(within(details).getByText('NSE:OLDCO')).toBeInTheDocument();
    expect(within(details).getByText('NSE:ZZZ')).toBeInTheDocument();

    await userEvent.click(within(details).getByRole('button', { name: 'Rejected rows (examples)' }));
    expect(within(details).getByText('ABC_1m.csv line 5: price is zero')).toBeInTheDocument();
  });

  it('shows the Dhan step in full: last fetch, bars added, the run’s result', async () => {
    renderData();

    const dhan = await openStep('Dhan Data API');
    expect(within(dhan).getByText(/^Last fetch/)).toHaveTextContent('Last fetch 12 Oct 2026, 09:20 IST');
    expect(within(dhan).getByText('4,500 bars added', { selector: 'p' })).toBeInTheDocument();
    expect(within(dhan).getByText('214 instruments · 4,500 bars added')).toBeInTheDocument();
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
      fake: withLast({ dhan: buildDataJob({ status: 'failed', error: 'Dhan refused the access token.', dhan }) }),
    });

    const details = await openStep('Dhan Data API');
    expect(within(details).getByText('Failed')).toBeInTheDocument();
    expect(within(details).getByText(/Dhan refused the access token\./)).toBeInTheDocument();
    expect(within(details).getByText('Code data.dhan_not_connected')).toBeInTheDocument();
  });

  it('shows why a step was skipped, and uses the code when there is no message', async () => {
    const dhan = buildDhanPhase({ status: 'skipped', message: null, code: 'data.dhan_not_connected' });
    renderData({ fake: withLast({ dhan: buildDataJob({ dhan }) }) });

    const details = await openStep('Dhan Data API');
    expect(within(details).getByText('Skipped')).toBeInTheDocument();
    expect(
      within(details).getByText(/Dhan is not connected for the Data account\. Reconnect it in Accounts\./),
    ).toBeInTheDocument();
  });

  it('warns when overlapping Dhan bars differ from the stored ones, and lists the errors on request', async () => {
    const dhan = buildDhanPhase({ overlapCompared: 1_000, overlapMismatched: 12, errors: ['NSE:ABC: timeout'] });
    renderData({ fake: withLast({ dhan: buildDataJob({ dhan }) }) });

    const details = await openStep('Dhan Data API');
    expect(within(details).getByText('12 of 1,000 overlapping bars differ from the stored ones')).toBeInTheDocument();
    await userEvent.click(within(details).getByRole('button', { name: '1 errors' }));
    expect(within(details).getByText('NSE:ABC: timeout')).toBeInTheDocument();
  });

  it('says why the Dhan step cannot fetch now', async () => {
    renderData({
      fake: createDataFake({
        overview: buildDataOverview({
          dhan: buildDhanOverview({ canFetch: false, reason: 'The Dhan sign-in has expired.', reasonCode: 'x' }),
        }),
      }),
    });

    expect(
      within(await openStep('Dhan Data API')).getByText('Cannot fetch now: The Dhan sign-in has expired.'),
    ).toBeInTheDocument();
  });

  it('shows the aggregates in full: bars per timeframe, last built, what waits', async () => {
    renderData({
      fake: createDataFake({
        overview: buildDataOverview({ aggregates: buildAggregatesOverview({ instrumentsPending: 7 }) }),
      }),
    });

    const aggregates = await openStep('Aggregates');
    const chips = within(within(aggregates).getByRole('list', { name: 'Bars per timeframe' }));
    expect(chips.getAllByRole('listitem')).toHaveLength(8);
    expect(chips.getByText('5m · 82.3L')).toBeInTheDocument();
    expect(chips.getByText('1M · 5K')).toBeInTheDocument();
    expect(within(aggregates).getByText(/^Last built/)).toHaveTextContent('Last built 12 Oct 2026, 09:30 IST');
    expect(within(aggregates).getByText('7 instruments have new bars not aggregated yet')).toBeInTheDocument();
  });

  it('shows no aggregates yet in words', async () => {
    renderData({
      fake: createDataFake({
        overview: buildDataOverview({ aggregates: buildAggregatesOverview({ timeframes: [], lastBuiltAt: null }) }),
      }),
    });

    expect(within(await openStep('Aggregates')).getByText('No aggregates built yet')).toBeInTheDocument();
  });
});
