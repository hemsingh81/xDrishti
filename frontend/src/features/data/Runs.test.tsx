import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { delay, http, HttpResponse } from 'msw';
import {
  buildCsvPhase,
  buildDataJob,
  buildDataOverview,
  buildDhanPhase,
  buildRunningJob,
  buildSkippedJob,
  dataJobId,
} from '@/test/fixtures';
import { createDataFake } from '@/test/dataFake';
import { openTab, renderData } from './testing';

// Poll fast so a run that ends fits in a test.
vi.mock('./lib/polling', () => ({ dataPollMs: 25 }));

const runs = async () => within(await screen.findByRole('list', { name: 'Recent runs' })).getAllByRole('listitem');

describe('Runs tab: the list', () => {
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
    renderData({ fake: createDataFake({ jobs: [buildDataJob(), failed] }), search: '?tab=runs' });

    const [first, second] = await runs();
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

  it('shows the last 20 runs', async () => {
    const many = Array.from({ length: 25 }, (_, n) => buildDataJob({ id: dataJobId(20 + n) }));
    renderData({ fake: createDataFake({ jobs: many }), search: '?tab=runs' });

    expect(await runs()).toHaveLength(20);
    expect(screen.getByText('The last 20 runs, newest first.')).toBeInTheDocument();
  });

  it('says so when no update has run yet', async () => {
    renderData({ fake: createDataFake({ jobs: [] }), search: '?tab=runs' });

    expect(await screen.findByText('No updates have run yet.')).toBeInTheDocument();
  });

  it('shows a loading state while the runs are read', async () => {
    renderData({ search: '?tab=runs', handlers: [http.get('/api/data/jobs', () => delay('infinite'))] });

    expect(await screen.findByText('Loading recent runs')).toBeInTheDocument();
  });

  it('says so when the runs cannot be loaded, and retries', async () => {
    const fake = createDataFake();
    fake.failures.jobs = true;
    renderData({ fake, search: '?tab=runs' });

    expect(await screen.findByText('The data service is not available.')).toBeInTheDocument();
    fake.failures.jobs = false;
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByRole('list', { name: 'Recent runs' })).toBeInTheDocument();
  });

  it('is reached from the tabs, and the runs are only read then', async () => {
    const fake = createDataFake();
    renderData({ fake });
    await screen.findByRole('tab', { name: 'Runs' });
    expect(screen.queryByRole('list', { name: 'Recent runs' })).not.toBeInTheDocument();

    await openTab('Runs');

    expect(await runs()).toHaveLength(1);
  });
});

describe('Runs tab: trigger and skipped runs', () => {
  it('names who started each run: Manual or Automatic', async () => {
    const daily = buildDataJob({ id: dataJobId(7), trigger: 'daily', steps: ['dhan', 'aggregate'] });
    renderData({ fake: createDataFake({ jobs: [daily, buildDataJob()] }), search: '?tab=runs' });

    const [first, second] = await runs();
    expect(within(first!).getByText('Automatic')).toBeInTheDocument();
    expect(within(second!).getByText('Manual')).toBeInTheDocument();
    expect(first).toHaveTextContent('Dhan · Aggregates');
  });

  it('shows a skipped automatic run with its reason and no steps', async () => {
    renderData({ fake: createDataFake({ jobs: [buildSkippedJob(), buildDataJob()] }), search: '?tab=runs' });

    const [first] = await runs();
    expect(first).toHaveTextContent('12 Oct 2026, 15:50 IST');
    expect(within(first!).getByText('Automatic')).toBeInTheDocument();
    expect(within(first!).getByText('Skipped')).toBeInTheDocument();
    expect(within(first!).getByText('The Data account is not connected.')).toBeInTheDocument();
    expect(within(first!).getByTestId('RemoveCircleOutlineOutlinedIcon')).toBeInTheDocument();
    expect(first).not.toHaveTextContent('All steps');
  });

  it('says when a skipped run has no recorded reason', async () => {
    renderData({ fake: createDataFake({ jobs: [buildSkippedJob({ error: null })] }), search: '?tab=runs' });

    expect((await runs())[0]).toHaveTextContent('No reason was recorded.');
  });

  it('notes a run that fetched the gaps again, and the gaps it found', async () => {
    const job = buildDataJob({
      refetchGaps: true,
      steps: ['dhan', 'aggregate'],
      dhan: { ...buildDataJob().dhan, gapsFound: 4, gapsFilled: 3, gapsUnfillable: 1 },
    });
    renderData({ fake: createDataFake({ jobs: [job] }), search: '?tab=runs' });

    const [first] = await runs();
    expect(first).toHaveTextContent('Dhan · Aggregates · gaps fetched again');
    expect(first).toHaveTextContent('gaps 4 found · 3 filled · 1 unfillable');
  });

  it('a run in the list that is still running shows as Running', async () => {
    renderData({ fake: createDataFake({ jobs: [buildRunningJob({ trigger: 'daily' })] }), search: '?tab=runs' });

    const [first] = await runs();
    expect(within(first!).getByText('Running')).toBeInTheDocument();
    expect(within(first!).getByText('Automatic')).toBeInTheDocument();
  });

  it('the watcher tells the owner when the run it followed ended as skipped', async () => {
    const running = buildRunningJob({ trigger: 'daily' });
    let reads = 0;
    renderData({
      fake: createDataFake({ jobs: [buildSkippedJob()] }),
      handlers: [
        http.get('/api/data/overview', () => {
          reads += 1;
          return HttpResponse.json(buildDataOverview({ runningJob: reads < 3 ? running : null }));
        }),
        http.get('/api/data/jobs/:id', () =>
          HttpResponse.json(buildSkippedJob({ id: running.id, error: 'An update was already running.' })),
        ),
      ],
    });

    expect(await screen.findByText('The update was skipped: An update was already running.')).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Update data' })).not.toHaveAttribute('aria-disabled');
    });
  });
});
