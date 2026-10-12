import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { sampleQualityRows } from '@/test/qualityFixtures';
import { createQualityFake, type QualityFake } from '@/test/qualityFake';
import { renderQuality } from './testing';

// Poll fast so a whole job fits in a test.
vi.mock('./lib/polling', () => ({ qualityPollMs: 25 }));

const [, , infy, hdfc, newco] = sampleQualityRows as [
  (typeof sampleQualityRows)[number],
  (typeof sampleQualityRows)[number],
  (typeof sampleQualityRows)[number],
  (typeof sampleQualityRows)[number],
  (typeof sampleQualityRows)[number],
];

const runButton = () => screen.getByRole('button', { name: 'Run checks' });
const ready = () => screen.findByRole('region', { name: 'Quality summary' });
const chip = (name: string) =>
  within(screen.getByRole('region', { name: 'Quality summary', hidden: true })).getByRole('button', {
    name,
    hidden: true,
  });
const inspect = async (symbol: string) => {
  await userEvent.click(await screen.findByRole('gridcell', { name: symbol }));
  const drawer = await screen.findByRole('dialog', { name: 'Inspect' });
  // The actions come with the checks (an instrument never checked has no Completeness section to wait for).
  await within(drawer).findByRole('button', { name: 'Check now' });
  return drawer;
};

/** Runs the job in progress to its end, one step at a time, so the page looks at each step. */
async function finish(fake: QualityFake) {
  while (fake.running) {
    const reads = fake.calls.overview;
    fake.advance();
    await waitFor(() => {
      expect(fake.calls.overview).toBeGreaterThan(reads);
    });
  }
}

describe('Run checks', () => {
  it('runs the checks, shows the progress, and reads the results again at the end with a toast', async () => {
    const fake = createQualityFake({ autoAdvance: false });
    renderQuality({ fake });
    await ready();
    expect(chip('Not checked 1')).toBeInTheDocument();
    await waitFor(() => {
      expect(runButton()).toBeEnabled();
    });

    await userEvent.click(runButton());

    // One slim line under the ribbon, a live region: what it does, how far, how the instruments came out so far.
    const progress = await screen.findByRole('progressbar', { name: 'Checking the data progress' });
    expect(screen.getAllByRole('status').some((region) => region.contains(progress))).toBe(true);
    expect(screen.getByText('Checking the data: 0 of 5 instruments')).toBeInTheDocument();
    expect(runButton()).toHaveAttribute('aria-disabled', 'true');
    expect(fake.calls.run).toEqual([{ instrumentId: null }]);
    fake.advance();
    expect(await screen.findByText('Checking the data: 2 of 5 instruments')).toBeInTheDocument();
    expect(await screen.findByText('So far: 1 passed · 0 warnings · 0 failed')).toBeInTheDocument();
    expect(screen.getByText('Checking NSE:INFY')).toBeInTheDocument();

    await finish(fake);

    expect(await screen.findByText('Checks finished: 2 passed · 1 warning · 2 failed.')).toBeInTheDocument();
    await waitFor(() => {
      expect(runButton()).not.toHaveAttribute('aria-disabled');
    });
    expect(screen.queryByRole('progressbar', { name: 'Checking the data progress' })).not.toBeInTheDocument();
    // The ribbon, the table and the runs were read again: the instrument never checked has a result now.
    await waitFor(() => {
      expect(chip('Not checked 0')).toBeInTheDocument();
    });
    expect(chip('Passed 2')).toBeInTheDocument();
    await waitFor(() => {
      expect(
        within(screen.getByRole('row', { name: new RegExp(`${newco.symbol}( |$)`) })).getByText(
          '12 Oct 2026, 16:05 IST',
        ),
      ).toBeInTheDocument();
    });
    await userEvent.click(screen.getByRole('button', { name: 'Run history' }));
    const history = await screen.findByRole('dialog', { name: 'Run history' });
    const runs = within(await within(history).findByRole('list', { name: 'Recent runs' })).getAllByRole('listitem');
    expect(runs).toHaveLength(2);
    expect(runs[0]).toHaveTextContent('Manual');
  });

  it('turns "No checks yet" into the results after the first run', async () => {
    const fake = createQualityFake({
      runs: [],
      rows: sampleQualityRows.map((r) => ({ ...r, status: 'notChecked', score: null })),
      autoAdvance: false,
    });
    renderQuality({ fake });
    expect(await screen.findByText('No checks yet — press Run checks')).toBeInTheDocument();
    await waitFor(() => {
      expect(runButton()).toBeEnabled();
    });

    await userEvent.click(runButton());
    await finish(fake);

    await waitFor(() => {
      expect(screen.queryByText('No checks yet — press Run checks')).not.toBeInTheDocument();
    });
    expect(await screen.findByText(/^Last run/)).toHaveTextContent('Manual · Succeeded');
  });

  it('does not start a second job from the disabled button', async () => {
    const fake = createQualityFake({ autoAdvance: false });
    renderQuality({ fake });
    await waitFor(() => {
      expect(runButton()).toBeEnabled();
    });

    await userEvent.click(runButton());
    await screen.findByRole('progressbar', { name: 'Checking the data progress' });
    await userEvent.click(runButton());

    expect(fake.calls.run).toHaveLength(1);
    await finish(fake);
  });

  it('shows a job that was already running when the page opened, and says how it ended', async () => {
    const fake = createQualityFake({ running: { steps: ['quality'] } });
    renderQuality({ fake });

    expect(await screen.findByRole('progressbar', { name: 'Checking the data progress' })).toBeInTheDocument();
    expect(runButton()).toHaveAttribute('aria-disabled', 'true');
    expect(await screen.findByText(/^Checks finished: /)).toBeInTheDocument();
    await waitFor(() => {
      expect(runButton()).not.toHaveAttribute('aria-disabled');
    });
  });

  it('says that a data update is running when the job is not a check (and names its step)', async () => {
    const fake = createQualityFake({ running: { steps: ['aggregate', 'quality'] } });
    renderQuality({ fake });

    expect(await screen.findByRole('progressbar', { name: 'Building the aggregates progress' })).toBeInTheDocument();
    expect(await screen.findByRole('progressbar', { name: 'Checking the data progress' })).toBeInTheDocument();
  });

  it('says what went wrong when the checks fail', async () => {
    const fake = createQualityFake({ failQuality: 'The quality step crashed.' });
    renderQuality({ fake });
    await waitFor(() => {
      expect(runButton()).toBeEnabled();
    });

    await userEvent.click(runButton());

    expect(await screen.findByText('The job failed: The quality step crashed.')).toBeInTheDocument();
    await waitFor(() => {
      expect(runButton()).not.toHaveAttribute('aria-disabled');
    });
  });

  it('says so when a job is already running (409) instead of starting one', async () => {
    const fake = createQualityFake({ startError: 'job_running' });
    renderQuality({ fake });
    await waitFor(() => {
      expect(runButton()).toBeEnabled();
    });

    await userEvent.click(runButton());

    expect(
      await screen.findByText(
        'An update or a check is already running. It finishes first; this page shows its progress.',
      ),
    ).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Close' }));
    expect(screen.queryByText(/is already running/)).not.toBeInTheDocument();
  });
});

describe('Inspect drawer: Re-fetch and Refresh aggregates', () => {
  it('Refresh aggregates runs with its progress in the drawer, then shows the instrument passing, with a toast', async () => {
    const fake = createQualityFake({ autoAdvance: false });
    renderQuality({ fake });
    const drawer = await inspect(infy.symbol);
    expect(within(drawer).getByRole('region', { name: 'Aggregates vs 1-minute' })).toHaveTextContent('2 differ');

    await userEvent.click(within(drawer).getByRole('button', { name: 'Refresh aggregates' }));

    expect(
      await within(drawer).findByRole('progressbar', { name: 'Building the aggregates progress' }),
    ).toBeInTheDocument();
    expect(within(drawer).getByText('Working on this instrument')).toBeInTheDocument();
    expect(within(drawer).getByRole('button', { name: 'Refresh aggregates' })).toHaveAttribute('aria-disabled', 'true');
    expect(within(drawer).getByRole('button', { name: 'Re-fetch' })).toHaveAttribute('aria-disabled', 'true');
    expect(fake.calls.refresh).toEqual([infy.instrumentId]);

    await finish(fake);

    expect(await screen.findByText('Aggregates refreshed and re-checked: all checks pass.')).toBeInTheDocument();
    // The drawer asks again: the aggregates match now.
    await waitFor(() => {
      expect(
        within(within(drawer).getByRole('region', { name: 'Aggregates vs 1-minute' })).getByText('Pass'),
      ).toBeInTheDocument();
    });
    const rows = within(within(drawer).getByRole('table', { name: 'Stored vs recomputed' }))
      .getAllByRole('row')
      .slice(1);
    for (const row of rows) expect(row).toHaveTextContent('Matches');
    expect(within(drawer).queryByRole('progressbar')).not.toBeInTheDocument();
    expect(within(drawer).getByRole('button', { name: 'Refresh aggregates' })).not.toHaveAttribute('aria-disabled');
    // The table and the tiles too (one instrument less fails).
    await waitFor(() => {
      expect(chip('Failed 1')).toBeInTheDocument();
    });
  });

  it('Re-fetch fetches, rebuilds and checks again: a stale instrument is fresh afterwards', async () => {
    const fake = createQualityFake({ autoAdvance: false });
    renderQuality({ fake });
    const drawer = await inspect(hdfc.symbol);
    expect(within(drawer).getByRole('region', { name: 'Freshness' })).toHaveTextContent('Behind by 2 trading days.');

    await userEvent.click(within(drawer).getByRole('button', { name: 'Re-fetch' }));

    expect(await within(drawer).findByRole('progressbar', { name: 'Fetching from Dhan progress' })).toBeInTheDocument();
    expect(fake.calls.refetch).toEqual([hdfc.instrumentId]);
    await finish(fake);

    expect(await screen.findByText('Re-fetched and re-checked: all checks pass.')).toBeInTheDocument();
    await waitFor(() => {
      expect(within(drawer).getByRole('region', { name: 'Freshness' })).toHaveTextContent('Up to date.');
    });
    expect(within(drawer).getByRole('region', { name: 'Freshness' })).toHaveTextContent('Pass');
  });

  it('says the instrument still has issues when the fix did not help', async () => {
    const fake = createQualityFake({ fixLeavesIssues: true });
    renderQuality({ fake });
    const drawer = await inspect(infy.symbol);

    await userEvent.click(within(drawer).getByRole('button', { name: 'Refresh aggregates' }));

    expect(await screen.findByText('Aggregates refreshed and re-checked: still has issues.')).toBeInTheDocument();
    expect(within(drawer).getByRole('region', { name: 'Aggregates vs 1-minute' })).toHaveTextContent('2 differ');
  });

  it('Check now checks just this instrument', async () => {
    const fake = createQualityFake({ autoAdvance: false });
    renderQuality({ fake });
    const drawer = await inspect(newco.symbol);

    await userEvent.click(within(drawer).getByRole('button', { name: 'Check now' }));

    expect(await within(drawer).findByRole('progressbar', { name: 'Checking the data progress' })).toBeInTheDocument();
    expect(fake.calls.run).toEqual([{ instrumentId: newco.instrumentId }]);
    await finish(fake);
    expect(await screen.findByText(/^Checked: /)).toBeInTheDocument();
  });

  it('Re-fetch is disabled with the reason when Dhan cannot be asked; the other buttons still work', async () => {
    const fake = createQualityFake({
      dhan: { canFetch: false, reason: 'The Data account is not connected.', reasonCode: 'data.dhan_not_connected' },
    });
    renderQuality({ fake });
    const drawer = await inspect(infy.symbol);

    const refetch = within(drawer).getByRole('button', { name: 'Re-fetch' });
    expect(refetch).toBeDisabled();
    expect(refetch).toHaveAccessibleDescription('Re-fetch is not available: The Data account is not connected.');
    expect(within(drawer).getByText('Re-fetch is not available: The Data account is not connected.')).toBeVisible();
    expect(within(drawer).getByRole('button', { name: 'Refresh aggregates' })).toBeEnabled();
    expect(within(drawer).getByRole('button', { name: 'Check now' })).toBeEnabled();
  });

  it.each([
    ['dhan_not_available', 'Re-fetch', 'Dhan cannot be asked for this instrument, so it cannot be re-fetched.'],
    [
      'dhan_not_connected',
      'Re-fetch',
      'Dhan is not connected for the Data account. Reconnect it in Accounts, then re-fetch.',
    ],
    [
      'job_running',
      'Refresh aggregates',
      'An update or a check is already running. It finishes first; this page shows its progress.',
    ],
    ['not_found', 'Refresh aggregates', 'That instrument no longer exists.'],
    ['server', 'Refresh aggregates', 'The data service is not available.'],
  ] as const)('shows the %s answer in the drawer', async (error, button, message) => {
    const fake = createQualityFake({ startError: error });
    renderQuality({ fake });
    const drawer = await inspect(infy.symbol);

    await userEvent.click(within(drawer).getByRole('button', { name: button }));

    expect(await within(drawer).findByText(message)).toBeInTheDocument();
    expect(screen.getAllByText(message)).toHaveLength(1);
    await userEvent.click(within(drawer).getByRole('button', { name: 'Close' }));
    expect(within(drawer).queryByText(message)).not.toBeInTheDocument();
  });

  it('tells that another job is running and keeps the buttons off until it ends', async () => {
    const fake = createQualityFake({ running: { steps: ['quality'] }, autoAdvance: false });
    renderQuality({ fake });
    const drawer = await inspect(infy.symbol);

    expect(
      await within(drawer).findByText('Another job is running; these buttons work again when it ends.'),
    ).toBeInTheDocument();
    expect(within(drawer).getByRole('button', { name: 'Refresh aggregates' })).toHaveAttribute('aria-disabled', 'true');
    await finish(fake);
    await waitFor(() => {
      expect(within(drawer).getByRole('button', { name: 'Refresh aggregates' })).not.toHaveAttribute('aria-disabled');
    });
  });
});
