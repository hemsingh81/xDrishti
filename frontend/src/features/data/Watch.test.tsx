import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { buildCorporateAction } from '@/test/actionFixtures';
import { createDataFake, type DataFake } from '@/test/dataFake';
import { buildDataOverview, buildEodOverview, sampleCalendarDays, sampleCalendarProposals } from '@/test/fixtures';
import { renderData } from './testing';

// Poll fast so a whole update fits in a test; the one-minute idle read is not mocked.
vi.mock('./lib/polling', () => ({ dataPollMs: 25 }));

afterEach(() => {
  vi.useRealTimers();
});

const ready = () => screen.findByRole('region', { name: 'Data status' });
const withEod = (nextRunAt: string | null, options: Parameters<typeof createDataFake>[0] = {}) =>
  createDataFake({
    autoAdvance: false,
    overview: buildDataOverview({ eod: buildEodOverview({ nextRunAt }) }),
    ...options,
  });

async function finish(fake: DataFake) {
  while (fake.running) {
    const reads = fake.calls.overview;
    fake.advance();
    await waitFor(() => {
      expect(fake.calls.overview).toBeGreaterThan(reads);
    });
  }
}

describe('Data & import: the screen follows what a run found', () => {
  const quiet = () => createDataFake({ calendar: { days: sampleCalendarDays, proposals: [] }, corporateActions: [] });
  const found = (fake: DataFake) => {
    // The run finds them while it works; nobody refocuses or reloads the page.
    fake.addProposals(...sampleCalendarProposals);
    fake.actions.seed(buildCorporateAction());
  };

  it('shows the calendar proposals a run produced as soon as it ends, on the open Calendar tab', async () => {
    const fake = quiet();
    renderData({ fake, search: '?tab=calendar' });
    await ready();
    await screen.findByRole('heading', { level: 2, name: 'Upcoming' });
    expect(screen.queryByRole('heading', { level: 2, name: /^Proposed/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Needs attention' })).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Update data' }));
    found(fake);
    await finish(fake);

    expect(await screen.findByRole('heading', { level: 2, name: 'Proposed (2)' })).toBeInTheDocument();
    expect(await screen.findByRole('tab', { name: 'Calendar, 2 to review' })).toBeInTheDocument();
    const attention = await screen.findByRole('region', { name: 'Needs attention' });
    expect(
      within(attention)
        .getAllByRole('link')
        .map((link) => link.textContent),
    ).toEqual(['2 calendar proposals', '1 suggested corporate action']);
  });

  it('shows the suggested actions a run produced as soon as it ends, on the open Corporate actions tab', async () => {
    const fake = quiet();
    renderData({ fake, search: '?tab=actions' });
    await ready();
    const panel = await screen.findByRole('tabpanel', { name: /^Corporate actions/ });
    await within(panel).findByText('No corporate actions yet.', { exact: false });
    expect(within(panel).queryByText('NSE:INFY')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Update data' }));
    found(fake);
    await finish(fake);

    expect(await within(panel).findByText('NSE:INFY')).toBeInTheDocument();
    expect(await screen.findByRole('tab', { name: 'Corporate actions, 1 suggested' })).toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: 'Suggested 1' })).toBeInTheDocument();
  });
});

describe('Data & import: an automatic run is noticed on an open page', () => {
  it('reads the overview once a minute while the page is open and idle, and follows a run that started by itself', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const fake = withEod(null);
    renderData({ fake });
    await ready();
    const reads = fake.calls.overview;

    fake.startAutomatic();
    await vi.advanceTimersByTimeAsync(30_000);
    expect(fake.calls.overview).toBe(reads);

    await vi.advanceTimersByTimeAsync(31_000);
    expect(await screen.findByText(/^Update in progress/)).toBeInTheDocument();
    expect(fake.calls.overview).toBeGreaterThan(reads);

    await finish(fake);
    expect(await screen.findByText(/^Data updated: /)).toBeInTheDocument();
  });

  it('reads the overview just after the next automatic run is due', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.setSystemTime(new Date('2026-10-13T10:19:30Z'));
    // 10:20:00 UTC is 15:50 in India.
    const fake = withEod('2026-10-13T15:50:00+05:30');
    renderData({ fake });
    await ready();
    const reads = fake.calls.overview;

    fake.startAutomatic();
    await vi.advanceTimersByTimeAsync(20_000);
    expect(fake.calls.overview).toBe(reads);
    expect(screen.queryByText(/^Update in progress/)).not.toBeInTheDocument();

    // 30 s to the run plus the grace: well before the minute is over.
    await vi.advanceTimersByTimeAsync(20_000);
    expect(await screen.findByText(/^Update in progress/)).toBeInTheDocument();
    expect(fake.calls.overview).toBeGreaterThan(reads);
  });

  it('does not wait for a run time that has passed already', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.setSystemTime(new Date('2026-10-13T10:30:00Z'));
    const fake = withEod('2026-10-13T15:50:00+05:30');
    renderData({ fake });
    await ready();
    const reads = fake.calls.overview;

    await vi.advanceTimersByTimeAsync(40_000);

    expect(fake.calls.overview).toBe(reads);
  });
});
