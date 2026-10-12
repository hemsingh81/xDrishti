import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import {
  buildBarsOverview,
  buildCoverageRow,
  buildDataOverview,
  buildDhanOverview,
  sampleCoverage,
} from '@/test/fixtures';
import { createDataFake, failureCode, failureMessage, type DataFake } from '@/test/dataFake';
import { openStep, openTab, ready, renderData } from './testing';

// Poll fast so a whole update fits in a test.
vi.mock('./lib/polling', () => ({ dataPollMs: 25 }));

const day = 86_400_000;
const behindOverview = () =>
  buildDataOverview({
    bars: buildBarsOverview({
      lastBar: new Date(Date.now() - 3 * day).toISOString(),
      stale: true,
      daysBehind: 2,
      instrumentsBehind: 1,
    }),
  });
const currentCoverage = sampleCoverage.map((row) =>
  row.status === 'behind'
    ? buildCoverageRow({ ...row, status: 'current', daysBehind: 0, lastBar: '2026-10-12T15:29:00+05:30' })
    : row,
);
afterEach(() => {
  vi.useRealTimers();
});

const updateButton = () => screen.getByRole('button', { name: 'Update data' });
const pipeline = () => screen.getByRole('region', { name: 'Pipeline' });
const step = (name: RegExp) => within(pipeline()).getByRole('listitem', { name });

/** Runs the job in progress to its end, one step at a time. */
async function finish(fake: DataFake) {
  while (fake.running) {
    const reads = fake.calls.overview;
    fake.advance();
    // Wait until the page has read the overview again, so it looks at each step.
    await waitFor(() => {
      expect(fake.calls.overview).toBeGreaterThan(reads);
    });
  }
}

describe('Update data: the run', () => {
  it('runs all steps, shows the progress and the data is current at the end', async () => {
    const fake = createDataFake({
      overview: behindOverview(),
      afterRun: { overview: { bars: buildBarsOverview() }, coverage: currentCoverage },
    });
    renderData({ fake });
    await ready();
    expect(screen.getByText(/^Behind by 2 trading days/)).toBeInTheDocument();
    expect(await screen.findByRole('row', { name: /NSE:INFY/ })).toHaveTextContent('Behind 2 d');

    await userEvent.click(updateButton());

    expect(await screen.findByRole('progressbar', { name: /progress$/ })).toBeInTheDocument();
    expect(updateButton()).toHaveAttribute('aria-disabled', 'true');
    expect(await screen.findByText(/^Data updated: /)).toBeInTheDocument();
    expect(fake.calls.start).toEqual([{}]);
    await waitFor(() => {
      expect(updateButton()).not.toHaveAttribute('aria-disabled');
    });
    expect(screen.queryByRole('progressbar', { name: /progress$/ })).not.toBeInTheDocument();
    // The strip, the table and the runs were read again.
    expect(await screen.findByText(/^Up to date/)).toBeInTheDocument();
    expect(screen.queryByText(/^Behind by/)).not.toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getByRole('row', { name: /NSE:INFY/ })).toHaveTextContent('Current');
    });
    await openTab('Runs');
    expect(within(await screen.findByRole('list', { name: 'Recent runs' })).getAllByRole('listitem')).toHaveLength(2);
  });

  it('shows each step in turn: counts and message while it runs, waiting for the ones after, the result when done', async () => {
    const fake = createDataFake({ autoAdvance: false });
    renderData({ fake });
    await ready();

    await userEvent.click(updateButton());

    const csv = await screen.findByRole('progressbar', { name: 'CSV files progress' });
    expect(csv).toHaveAttribute('aria-valuenow', '0');
    expect(screen.getByRole('status', { name: 'Update progress' })).toHaveTextContent(
      'Update in progress, loading the CSV files.',
    );
    expect(within(step(/Dhan Data API/)).getByText('Waiting for the earlier steps')).toBeInTheDocument();
    expect(within(step(/Aggregates/)).getByText('Waiting for the earlier steps')).toBeInTheDocument();
    expect(within(step(/CSV files/)).getByText('Running')).toBeInTheDocument();

    fake.advance();
    expect(await within(step(/CSV files/)).findByText('107 of 214 files')).toBeInTheDocument();
    expect(within(step(/CSV files/)).getByText('Loading RELIANCE_1m.csv.gz')).toBeInTheDocument();
    expect(screen.getByRole('progressbar', { name: 'CSV files progress' })).toHaveAttribute('aria-valuenow', '50');

    fake.advance();
    expect(await screen.findByRole('progressbar', { name: 'Dhan Data API progress' })).toBeInTheDocument();
    expect(screen.queryByRole('progressbar', { name: 'CSV files progress' })).not.toBeInTheDocument();
    expect(screen.getByRole('status', { name: 'Update progress' })).toHaveTextContent(
      'fetching from the Dhan Data API',
    );
    expect(within(step(/CSV files/)).getByText('Succeeded')).toBeInTheDocument();
    expect(within(step(/Aggregates/)).getByText('Waiting for the earlier steps')).toBeInTheDocument();

    await finish(fake);
    expect(await screen.findByText(/^Data updated: /)).toBeInTheDocument();
    expect(screen.queryByRole('progressbar', { name: /progress$/ })).not.toBeInTheDocument();
  });

  it('shows a job that is already running when the page opens, and follows it to the end', async () => {
    const fake = createDataFake({ autoAdvance: false, running: { steps: ['csv', 'aggregate'] } });
    renderData({ fake });
    await ready();

    expect(await screen.findByRole('progressbar', { name: 'CSV files progress' })).toBeInTheDocument();
    expect(updateButton()).toHaveAttribute('aria-disabled', 'true');
    expect(screen.getByRole('button', { name: 'More update options' })).toHaveAttribute('aria-disabled', 'true');
    // A step the job does not run shows its last result, not a progress bar.
    expect(within(step(/Dhan Data API/)).queryByRole('progressbar')).not.toBeInTheDocument();
    expect(within(step(/Dhan Data API/)).getByText('Succeeded')).toBeInTheDocument();

    await userEvent.click(updateButton());
    expect(fake.calls.start).toEqual([]);

    await finish(fake);
    expect(await screen.findByText(/^Data updated: /)).toBeInTheDocument();
    await waitFor(() => {
      expect(updateButton()).not.toHaveAttribute('aria-disabled');
    });
  });

  it('stops reading the overview every few seconds once nothing runs', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const fake = createDataFake({ autoAdvance: false });
    renderData({ fake });
    await ready();
    const reads = fake.calls.overview;

    // Far more than the poll interval (the 25 ms above): a page that still polled would read the overview many times.
    await vi.advanceTimersByTimeAsync(10_000);

    expect(fake.calls.overview).toBe(reads);
  });
});

describe('Update data: the menu', () => {
  it.each([
    ['Load CSV files only', { steps: ['csv'] }],
    ['Fetch from Dhan only', { steps: ['dhan'] }],
    ['Rebuild aggregates only', { steps: ['aggregate'] }],
    ['Re-fetch gaps', { steps: ['dhan', 'aggregate'], refetchGaps: true }],
    ['Re-import all CSV files', { steps: ['csv'], forceCsv: true }],
  ])('"%s" starts the right job', async (name, body) => {
    const fake = createDataFake({ autoAdvance: false });
    renderData({ fake });
    await ready();

    await userEvent.click(screen.getByRole('button', { name: 'More update options' }));
    await userEvent.click(await screen.findByRole('menuitem', { name: new RegExp(`^${name}`) }));

    await waitFor(() => {
      expect(fake.calls.start).toEqual([body]);
    });
    // Only the requested steps run.
    expect(await screen.findByText(/^Update in progress/)).toBeInTheDocument();
  });

  it('describes what each option does', async () => {
    renderData();
    await ready();

    await userEvent.click(screen.getByRole('button', { name: 'More update options' }));

    expect(await screen.findByText('Read new or changed files from the CSV folder')).toBeInTheDocument();
    expect(screen.getByText('Add the 1-minute bars missing since the last bar')).toBeInTheDocument();
    expect(screen.getByText('Build 5m to 1M bars from the 1-minute bars')).toBeInTheDocument();
    expect(screen.getByText('Load every file again, even if unchanged (CSV step only)')).toBeInTheDocument();
    expect(
      screen.getByText('Fetch the sessions with missing bars again, unfillable ones too, then rebuild the aggregates'),
    ).toBeInTheDocument();
  });
});

describe('Update data: problems', () => {
  it('says an update is already running when the API answers 409 job_running', async () => {
    renderData({ fake: createDataFake({ startError: 'job_running' }) });
    await ready();

    await userEvent.click(updateButton());

    expect(await screen.findByRole('alert')).toHaveTextContent('An update is already running.');
  });

  it('says to refresh the instrument list first on 409 master_missing', async () => {
    renderData({ fake: createDataFake({ startError: 'master_missing' }) });
    await ready();

    await userEvent.click(updateButton());

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'The instrument list is missing. Refresh it first on the Instruments page, then update the data.',
    );
    expect(updateButton()).not.toHaveAttribute('aria-disabled');
  });

  it('shows the API’s title for any other failure and lets it be dismissed', async () => {
    renderData({ fake: createDataFake({ startError: 'server' }) });
    await ready();

    await userEvent.click(updateButton());

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('The data service is not available.');
    await userEvent.click(within(alert).getByRole('button', { name: 'Close' }));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('reports a failed step with its message and code, and the steps after it as skipped', async () => {
    const fake = createDataFake({ failPhase: 'dhan' });
    renderData({ fake });
    await ready();

    await userEvent.click(updateButton());

    expect(await screen.findByText(`The update failed: ${failureMessage}`)).toBeInTheDocument();
    await waitFor(() => {
      expect(updateButton()).not.toHaveAttribute('aria-disabled');
    });
    const dhan = step(/Dhan Data API/);
    expect(await within(dhan).findByText('Failed')).toBeInTheDocument();
    expect(within(dhan).getByText(failureMessage)).toBeInTheDocument();
    expect(within(step(/Aggregates/)).getByText('Skipped')).toBeInTheDocument();
    expect(within(step(/Aggregates/)).getByText('An earlier step failed.')).toBeInTheDocument();
    // The code is in the step's details.
    expect(within(await openStep('Dhan Data API')).getByText(`Code ${failureCode}`)).toBeInTheDocument();
    await userEvent.keyboard('{Escape}');
    await openTab('Runs');
    await waitFor(() => {
      const [latest] = within(screen.getByRole('list', { name: 'Recent runs' })).getAllByRole('listitem');
      expect(latest).toHaveTextContent('Failed');
      expect(latest).toHaveTextContent(failureMessage);
    });
  });

  it('shows the Dhan step as skipped, with the reason, when it cannot fetch', async () => {
    const reason = 'The Dhan sign-in has expired.';
    const fake = createDataFake({
      overview: buildDataOverview({
        dhan: buildDhanOverview({ canFetch: false, reason, reasonCode: 'data.dhan_not_connected' }),
      }),
    });
    renderData({ fake });
    await ready();

    await userEvent.click(updateButton());

    await screen.findByText(/^Data updated/);
    const dhan = step(/Dhan Data API/);
    expect(await within(dhan).findByText('Skipped')).toBeInTheDocument();
    expect(within(dhan).getByText(reason)).toBeInTheDocument();
  });
});
