import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http } from 'msw';
import { buildLoadedCsvFile, sampleCsvFiles } from '@/test/csvFixtures';
import { createDataFake, type DataFake } from '@/test/dataFake';
import { buildCsvOverview, buildCsvPhase, buildDataJob, buildDataOverview, dataJobId } from '@/test/fixtures';
import { problemResponse } from '@/test/server';
import { findToast } from '@/test/toast';
import { openCsvReview, openStep, openTab, renderData } from './testing';

vi.mock('./lib/polling', () => ({ dataPollMs: 25 }));

const reliance = 'RELIANCE_1m.csv.gz';
const fakeOf = (options: Parameters<typeof createDataFake>[0] = {}) =>
  createDataFake({
    overview: buildDataOverview({ csv: buildCsvOverview({ filesPending: 3, filesLoaded: 5, filesTotal: 8 }) }),
    ...options,
  });

async function openUndo(name = reliance) {
  const dialog = await openCsvReview();
  await userEvent.click(await within(dialog).findByRole('button', { name: `Undo load of ${name}` }));
  return { dialog, confirm: await screen.findByRole('dialog', { name: `Undo the load of ${name}?` }) };
}

async function finish(fake: DataFake) {
  while (fake.running) {
    const reads = fake.calls.overview;
    fake.advance();
    await waitFor(() => {
      expect(fake.calls.overview).toBeGreaterThan(reads);
    });
  }
}

describe('Undo the load of a CSV file', () => {
  it('has Undo only on files that were loaded', async () => {
    renderData({ fake: fakeOf() });
    const dialog = await openCsvReview();
    await within(dialog).findByRole('grid', { name: 'CSV files' });

    expect(within(dialog).getByRole('button', { name: `Undo load of ${reliance}` })).toBeEnabled();
    expect(within(dialog).getByRole('button', { name: 'Undo load of INFY_1m.csv.gz' })).toBeEnabled();
    expect(within(dialog).queryByRole('button', { name: 'Undo load of ALPHA_1m.csv' })).not.toBeInTheDocument();
    expect(within(dialog).getAllByRole('button', { name: /^Undo load of / })).toHaveLength(3);
  });

  it('says exactly what is removed and what is not before it does anything', async () => {
    const fake = fakeOf();
    renderData({ fake });
    const { confirm } = await openUndo();

    expect(confirm).toHaveTextContent(
      'removes the CSV bars of NSE:RELIANCE from 1 Apr 2024, 09:15 IST to 30 Apr 2026, 15:29 IST',
    );
    expect(confirm).toHaveTextContent('the 12,34,567 rows this file loaded');
    const list = within(confirm).getByRole('list', { name: 'What the undo does' });
    expect(within(list).getByText('Bars that came from Dhan stay.')).toBeInTheDocument();
    expect(within(list).getByText(/aggregates of NSE:RELIANCE are rebuilt/)).toBeInTheDocument();
    expect(within(list).getByText(/becomes pending again/)).toBeInTheDocument();
    expect(within(list).getByText('Values that an earlier load overwrote are not restored.')).toBeInTheDocument();
    expect(within(list).getByText(/stays tracked/)).toBeInTheDocument();

    await userEvent.click(within(confirm).getByRole('button', { name: 'Cancel' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: `Undo the load of ${reliance}?` })).not.toBeInTheDocument();
    });
    expect(fake.calls.undo).toEqual([]);
  });

  it('says so when the range of the file was not recorded', async () => {
    const files = [
      ...sampleCsvFiles().filter((file) => file.name !== reliance),
      buildLoadedCsvFile({ firstBar: null, lastBar: null, rows: null }),
    ];
    renderData({ fake: fakeOf({ csv: { files } }) });
    const { confirm } = await openUndo();
    expect(confirm).toHaveTextContent('over the whole range of the instrument');
    expect(confirm).not.toHaveTextContent('rows this file loaded');
  });

  it('starts the undo job, follows it, shows the result in the CSV step and the runs, and toasts when done', async () => {
    const fake = fakeOf({ autoAdvance: false });
    renderData({ fake });
    const { dialog, confirm } = await openUndo();

    await userEvent.click(within(confirm).getByRole('button', { name: 'Undo load' }));

    await waitFor(() => {
      expect(fake.calls.undo).toEqual([reliance]);
    });
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(dialog).not.toBeInTheDocument();
    expect(fake.running?.undoCsvFile).toBe(reliance);
    expect(await screen.findByRole('progressbar', { name: 'CSV files progress' })).toBeInTheDocument();

    await finish(fake);
    const message = `CSV load of ${reliance} undone, 1,234,567 bars removed`;
    expect(await findToast()).toHaveTextContent(message);

    const csv = await openStep('CSV files');
    expect(csv).toHaveTextContent(message);
    expect(csv).not.toHaveTextContent('repaired');
    await userEvent.keyboard('{Escape}');
    await openTab('Runs');
    const runs = within(await screen.findByRole('list', { name: 'Recent runs' }));
    await waitFor(() => {
      expect(runs.getAllByRole('listitem')).toHaveLength(2);
    });
    const latest = runs.getAllByRole('listitem')[0];
    expect(latest).toHaveTextContent(`Undo of ${reliance}`);
    expect(latest).toHaveTextContent('12,34,567 bars removed');

    // The file is pending again in the list.
    const reopened = await openCsvReview();
    expect(await within(reopened).findByRole('button', { name: /^Pending 6/ })).toBeInTheDocument();
    expect(within(reopened).queryByRole('button', { name: `Undo load of ${reliance}` })).not.toBeInTheDocument();
  });

  it('keeps the dialog open and says so when the file was never loaded (404)', async () => {
    const fake = fakeOf();
    renderData({ fake });
    const { confirm } = await openUndo();
    // Another tab undid it meanwhile.
    fake.csv.markPending(reliance);

    await userEvent.click(within(confirm).getByRole('button', { name: 'Undo load' }));

    expect(await within(confirm).findByText(/never loaded, or is no longer in the folder/)).toBeInTheDocument();
    expect(fake.running).toBeNull();
    // The list was reloaded: the Undo button is gone.
    await userEvent.click(within(confirm).getByRole('button', { name: 'Cancel' }));
    await waitFor(() => {
      expect(screen.queryByRole('button', { name: `Undo load of ${reliance}` })).not.toBeInTheDocument();
    });
  });

  it('says an update is running when another job holds the data (409)', async () => {
    const fake = fakeOf();
    renderData({
      fake,
      handlers: [
        http.post('/api/data/csv/files/:name/undo', () =>
          problemResponse({ title: 'An update is already running.', code: 'data.job_running' }, 409),
        ),
      ],
    });
    const { confirm } = await openUndo();

    await userEvent.click(within(confirm).getByRole('button', { name: 'Undo load' }));

    expect(
      await within(confirm).findByText('An update is already running. Wait for it to finish.'),
    ).toBeInTheDocument();
  });
});

describe('An undo in the pipeline and the runs', () => {
  const undoJob = buildDataJob({
    steps: ['csv', 'aggregate'],
    undoCsvFile: 'TCS_1m.csv.gz',
    csv: buildCsvPhase({
      filesLoaded: 0,
      rows: 0,
      inserted: 0,
      repaired: 0,
      barsRemoved: 5_000,
      message: 'CSV load of TCS_1m.csv.gz undone, 5,000 bars removed',
    }),
  });

  it('shows what the last CSV job removed instead of files loaded', async () => {
    const overview = buildDataOverview({ lastJobs: { csv: undoJob, dhan: buildDataJob(), aggregate: undoJob } });
    renderData({ fake: createDataFake({ overview, jobs: [undoJob] }) });

    const csv = await openStep('CSV files');
    expect(csv).toHaveTextContent('CSV load of TCS_1m.csv.gz undone, 5,000 bars removed');
    expect(csv).not.toHaveTextContent('repaired');
    await userEvent.keyboard('{Escape}');

    await openTab('Runs');
    const runs = within(await screen.findByRole('list', { name: 'Recent runs' }));
    expect(runs.getByText('Undo of TCS_1m.csv.gz')).toBeInTheDocument();
    expect(runs.getByText(/^5,000 bars removed/)).toBeInTheDocument();
  });

  it('labels a run that loaded chosen files', async () => {
    const chosen = buildDataJob({ csvFiles: ['A_1m.csv', 'B_1m.csv'], forceCsv: true, steps: ['csv', 'aggregate'] });
    renderData({ fake: createDataFake({ jobs: [chosen] }), search: '?tab=runs' });
    const runs = within(await screen.findByRole('list', { name: 'Recent runs' }));
    expect(runs.getByText(/2 chosen CSV files \(loaded again\)/)).toBeInTheDocument();
  });
});

describe('Files that cover the same time', () => {
  const withOverlap = () => [
    ...sampleCsvFiles().filter((file) => file.name !== reliance),
    buildLoadedCsvFile({ overlapsWith: ['RELIANCE_2025_1m.csv', 'RELIANCE_old_1m.csv.gz'] }),
  ];

  it('names the other files in the list, only for the files that overlap', async () => {
    renderData({ fake: fakeOf({ csv: { files: withOverlap() } }) });
    const dialog = await openCsvReview();

    const row = await within(dialog).findByRole('row', { name: /RELIANCE_1m\.csv\.gz/ });
    expect(row).toHaveTextContent('Also covers the same time: RELIANCE_2025_1m.csv, RELIANCE_old_1m.csv.gz');
    expect(within(dialog).getAllByText(/^Also covers the same time:/)).toHaveLength(1);
  });

  it('says in the Undo question that the other files’ bars in the time range go too', async () => {
    renderData({ fake: fakeOf({ csv: { files: withOverlap() } }) });
    const { confirm } = await openUndo();

    expect(confirm).toHaveTextContent(
      'Undoing this file also removes the CSV bars of: RELIANCE_2025_1m.csv, RELIANCE_old_1m.csv.gz.',
    );
    expect(confirm).toHaveTextContent('Undo removes the instrument’s CSV bars in this file’s time range');
  });

  it('says nothing about overlaps for a file that has none', async () => {
    renderData({ fake: fakeOf() });
    const { confirm } = await openUndo();

    expect(confirm).not.toHaveTextContent('also removes the CSV bars of');
  });

  it('shows the warning of the undo job in the CSV step and in Recent runs when it ends', async () => {
    const fake = fakeOf({ autoAdvance: false, csv: { files: withOverlap() } });
    renderData({ fake });
    const { confirm } = await openUndo();

    await userEvent.click(within(confirm).getByRole('button', { name: 'Undo load' }));
    await waitFor(() => {
      expect(fake.running?.undoCsvFile).toBe(reliance);
    });
    await finish(fake);

    const warning = 'The undo also removed CSV bars loaded from: RELIANCE_2025_1m.csv, RELIANCE_old_1m.csv.gz.';
    const chip = await screen.findByRole('listitem', { name: /CSV files/ });
    expect(await within(chip).findByText('1 warning')).toBeInTheDocument();
    const csv = await openStep('CSV files');
    expect(await within(csv).findByText(warning)).toBeInTheDocument();
    await userEvent.keyboard('{Escape}');
    await openTab('Runs');
    const runs = within(await screen.findByRole('list', { name: 'Recent runs' }));
    await waitFor(() => {
      expect(runs.getAllByRole('listitem')[0]).toHaveTextContent(warning);
    });
  });
});

describe('Warnings of a job', () => {
  it('shows what a finished job noted in Recent runs and not for a job that noted nothing', async () => {
    const noted = buildDataJob({ warnings: ['3 files were skipped because they were empty.'] });
    const quiet = buildDataJob({ id: dataJobId(9), startedAt: '2026-10-11T09:00:00+05:30' });
    renderData({ fake: createDataFake({ jobs: [noted, quiet] }), search: '?tab=runs' });

    const runs = within(await screen.findByRole('list', { name: 'Recent runs' }));
    expect(runs.getAllByText('3 files were skipped because they were empty.')).toHaveLength(1);
    expect(runs.getAllByRole('list', { name: 'Warnings' })).toHaveLength(1);
  });
});
