import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createQualityFake, type QualityFake } from '@/test/qualityFake';
import { renderQuality } from './testing';

// Poll fast so a whole job fits in a test.
vi.mock('./lib/polling', () => ({ qualityPollMs: 25 }));

const open = async () => {
  await screen.findByRole('region', { name: 'Quality summary' });
  const button = screen.getByRole('button', { name: 'Fix issues' });
  await waitFor(() => {
    expect(button).toBeEnabled();
  });
  await userEvent.click(button);
  return screen.findByRole('dialog', { name: 'Fix data issues' });
};

async function finish(fake: QualityFake) {
  while (fake.running) {
    const reads = fake.calls.overview;
    fake.advance();
    await waitFor(() => {
      expect(fake.calls.overview).toBeGreaterThan(reads);
    });
  }
}

describe('Fix issues', () => {
  it('analyses the data and lists what the fix will do', async () => {
    renderQuality({ fake: createQualityFake({ openGaps: 3 }) });

    const dialog = await open();

    const steps = within(within(dialog).getByRole('list', { name: 'Fix steps' })).getAllByRole('listitem');
    expect(steps).toHaveLength(3);
    expect(steps[0]).toHaveTextContent('Fetch missing bars from Dhan');
    expect(steps[0]).toHaveTextContent('3 sessions with missing bars');
    expect(steps[1]).toHaveTextContent('Rebuild the aggregates');
    expect(steps[2]).toHaveTextContent('Check again');
    expect(within(dialog).getByRole('button', { name: 'Run fix' })).toBeEnabled();
  });

  it('runs fetch, aggregate and check as one job, with progress and a toast at the end', async () => {
    const fake = createQualityFake({ autoAdvance: false, openGaps: 1 });
    renderQuality({ fake });
    const dialog = await open();

    await userEvent.click(within(dialog).getByRole('button', { name: 'Run fix' }));

    expect(fake.calls.fixAll).toEqual([{ steps: ['dhan', 'aggregate', 'quality'], refetchGaps: true }]);
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Fix data issues' })).not.toBeInTheDocument();
    });
    expect(await screen.findByRole('progressbar', { name: /progress$/ })).toBeInTheDocument();
    await finish(fake);
    expect(await screen.findByText(/^Fix finished:/)).toBeInTheDocument();
  });

  it('leaves Dhan out, with the reason, when it cannot be asked', async () => {
    const fake = createQualityFake({ dhan: { canFetch: false, reason: 'The Data account is not connected.' } });
    renderQuality({ fake });
    const dialog = await open();

    expect(within(dialog).getByText(/Left out: The Data account is not connected\./)).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Run fix' }));

    await waitFor(() => {
      expect(fake.calls.fixAll).toEqual([{ steps: ['aggregate', 'quality'], refetchGaps: false }]);
    });
  });
});
