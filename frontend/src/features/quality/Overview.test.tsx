import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http } from 'msw';
import { appliedCss } from '@/test/css';
import { buildQualityRun, qualityRunId, sampleQualityRows } from '@/test/qualityFixtures';
import { createQualityFake } from '@/test/qualityFake';
import { renderQuality } from './testing';
import { pendingResponse } from '@/test/gate';

const ready = () => screen.findByRole('region', { name: 'Quality summary' });
const gateChip = () => screen.findByRole('button', { name: /^Gate: / });
const openGate = async () => {
  await userEvent.click(await gateChip());
  return screen.findByRole('dialog', { name: /^Gate: / });
};
const openHistory = async () => {
  await userEvent.click(screen.getByRole('button', { name: 'Run history' }));
  return screen.findByRole('dialog', { name: 'Run history' });
};

describe('Data quality: header and last run', () => {
  it('shows the title, the Run checks button and the last run with its time, trigger and result', async () => {
    renderQuality();

    expect(await screen.findByRole('heading', { level: 1, name: 'Data quality' })).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Run checks' })).toBeEnabled();
    });
    expect(await screen.findByText(/^Last run/)).toHaveTextContent('Last run 12 Oct 15:52 · Automatic · Succeeded');
  });

  it.each([
    ['manual', 'Manual'],
    ['daily', 'Automatic'],
    ['after_job', 'After update'],
  ])('names the trigger %s as %s', async (trigger, word) => {
    renderQuality({ fake: createQualityFake({ runs: [buildQualityRun({ trigger })] }) });

    expect(await screen.findByText(/^Last run/)).toHaveTextContent(`· ${word} ·`);
  });

  it('says why the last run failed', async () => {
    const fake = createQualityFake({
      runs: [buildQualityRun({ status: 'failed', error: 'The database was not reachable.' })],
    });
    renderQuality({ fake });

    expect(await screen.findByText(/^Last run/)).toHaveTextContent('Failed — The database was not reachable.');
  });

  it('says "No checks yet — press Run checks" as the empty state of the table before the first run', async () => {
    const fake = createQualityFake({
      runs: [],
      rows: sampleQualityRows.map((r) => ({ ...r, status: 'notChecked', score: null })),
    });
    renderQuality({ fake });

    expect(await screen.findByText('No checks yet — press Run checks')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Instruments' })).toHaveTextContent('No checks yet — press Run checks');
    expect(screen.queryByRole('grid', { name: 'Instruments' })).not.toBeInTheDocument();
    expect(screen.queryByText(/^Last run/)).not.toBeInTheDocument();
    expect(await screen.findByRole('button', { name: 'Not checked 5' })).toBeInTheDocument();
  });

  it('lists the instruments when some have results although no full run was recorded', async () => {
    renderQuality({ fake: createQualityFake({ runs: [] }) });

    expect(await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' })).toBeInTheDocument();
    expect(screen.queryByText('No checks yet — press Run checks')).not.toBeInTheDocument();
  });

  it('says so when the overview cannot be loaded, and Retry loads it', async () => {
    const fake = createQualityFake();
    fake.failures.overview = true;
    renderQuality({ fake });

    expect(await screen.findByText('The data service is not available.')).toBeInTheDocument();
    fake.failures.overview = false;
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));

    expect(await ready()).toBeInTheDocument();
  });

  it('shows a loading indicator while the overview loads', () => {
    renderQuality();

    expect(screen.getByText('Loading data quality')).toBeInTheDocument();
  });

  it('leaves the slot for the corporate actions alert empty until that feature exists', async () => {
    renderQuality();
    await ready();

    expect(screen.queryByText(/corporate action/i)).not.toBeInTheDocument();
  });
});

describe('Data quality: layout', () => {
  it('fills the viewport from md with the table taking the rest of the height, and the ribbon is one wrapping row', async () => {
    renderQuality();
    const ribbon = await ready();

    const page = ribbon.parentElement!;
    expect(appliedCss(page).base).toContain('display: flex');
    expect(appliedCss(page).responsive.map((r) => `${r.query} ${r.css}`)).toEqual([
      expect.stringMatching(/^\(min-width:900px\) height: 100%;/),
    ]);
    expect(appliedCss(ribbon).base).toMatch(/flex-wrap: wrap/);
    const card = screen.getByRole('region', { name: 'Instruments' });
    expect(appliedCss(card).base).toMatch(/flex: 1 1 auto/);
    expect(appliedCss(card).responsive.some((r) => /min-width:900px.*height: auto/.test(`${r.query} ${r.css}`))).toBe(
      true,
    );
  });

  it('has no tiles, no gate card and no big recent-runs card any more', async () => {
    renderQuality();
    await ready();

    expect(screen.queryByRole('region', { name: /^Gate:/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Recent runs' })).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Instruments' })).not.toBeInTheDocument();
    expect(document.querySelectorAll('.MuiCard-root')).toHaveLength(1);
  });
});

describe('Data quality: summary ribbon', () => {
  it('shows the score and the instruments by result as chips with counts, in one region', async () => {
    renderQuality();
    const ribbon = await ready();

    expect(ribbon).toHaveTextContent('Score88of 100');
    const chips = within(within(ribbon).getByRole('group', { name: 'Filter by result' })).getAllByRole('button');
    expect(chips.map((chip) => chip.textContent)).toEqual([
      'All 5',
      'Passed 1',
      'Warnings 1',
      'Failed 2',
      'Not checked 1',
    ]);
    expect(chips.map((chip) => chip.getAttribute('aria-pressed'))).toEqual([
      'true',
      'false',
      'false',
      'false',
      'false',
    ]);
  });

  it('says every result in words with an icon, never by colour alone', async () => {
    renderQuality();
    const ribbon = await ready();

    const group = within(ribbon).getByRole('group', { name: 'Filter by result' });
    expect(
      within(within(group).getByRole('button', { name: 'Passed 1' })).getByTestId('CheckCircleOutlinedIcon'),
    ).toBeInTheDocument();
    expect(
      within(within(group).getByRole('button', { name: 'Warnings 1' })).getByTestId('WarningAmberOutlinedIcon'),
    ).toBeInTheDocument();
    expect(
      within(within(group).getByRole('button', { name: 'Failed 2' })).getByTestId('ErrorOutlineOutlinedIcon'),
    ).toBeInTheDocument();
    expect(
      within(within(group).getByRole('button', { name: 'Not checked 1' })).getByTestId(
        'RemoveCircleOutlineOutlinedIcon',
      ),
    ).toBeInTheDocument();
  });

  it('shows a dash for the score before the first run', async () => {
    renderQuality({ fake: createQualityFake({ runs: [], rows: [] }) });
    const ribbon = await ready();

    expect(ribbon).toHaveTextContent('Score—not scored yet');
  });

  it('filters the table by a chip, marks it pressed, and clears the filter when it is pressed again', async () => {
    const { router } = renderQuality();
    await ready();

    await userEvent.click(screen.getByRole('button', { name: 'Warnings 1' }));

    expect(router.state.location.search).toBe('?status=warning');
    expect(screen.getByRole('button', { name: 'Warnings 1' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button', { name: 'All 5' })).toHaveAttribute('aria-pressed', 'false');
    await waitFor(() => {
      expect(screen.queryByRole('gridcell', { name: 'NSE:RELIANCE' })).not.toBeInTheDocument();
    });
    expect(screen.getByRole('gridcell', { name: 'NSE:TCS' })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Warnings 1' }));

    expect(router.state.location.search).toBe('');
    expect(screen.getByRole('button', { name: 'Warnings 1' })).toHaveAttribute('aria-pressed', 'false');
    expect(await screen.findByRole('gridcell', { name: 'NSE:RELIANCE' })).toBeInTheDocument();
  });

  it('switches between the results, and All shows everything again', async () => {
    const { router } = renderQuality();
    await ready();

    await userEvent.click(screen.getByRole('button', { name: 'Failed 2' }));
    expect(router.state.location.search).toBe('?status=fail');
    await userEvent.click(screen.getByRole('button', { name: 'Passed 1' }));
    expect(router.state.location.search).toBe('?status=pass');
    expect(screen.getByRole('button', { name: 'Failed 2' })).toHaveAttribute('aria-pressed', 'false');

    await userEvent.click(screen.getByRole('button', { name: 'All 5' }));

    expect(router.state.location.search).toBe('');
    expect(screen.getByRole('button', { name: 'All 5' })).toHaveAttribute('aria-pressed', 'true');
  });

  it('keeps the chips in step with a shared link and with the counts after a search', async () => {
    renderQuality({ search: '?status=fail&q=infy' });

    expect(await screen.findByRole('button', { name: 'Failed 2' })).toHaveAttribute('aria-pressed', 'true');
    await waitFor(() => {
      expect(screen.getByRole('gridcell', { name: 'NSE:INFY' })).toBeInTheDocument();
    });
    expect(screen.queryByRole('gridcell', { name: 'NSE:HDFCBANK' })).not.toBeInTheDocument();
  });

  it('says which window the checks cover and the limits in force', async () => {
    renderQuality();
    const ribbon = await ready();

    expect(within(ribbon).getByText('Last 5 sessions, 6–12 Oct 2026')).toBeInTheDocument();
    expect(
      within(ribbon).getByText('Spike above 20 × the median move · missing minutes above 1 %'),
    ).toBeInTheDocument();
  });
});

describe('Data quality: the gate', () => {
  it('is a chip with an icon and a word that opens a labelled popover; Blocked shows the reason', async () => {
    renderQuality();

    const chip = await gateChip();
    expect(chip).toHaveTextContent('Gate: Blocked');
    expect(within(chip).getByTestId('BlockOutlinedIcon')).toBeInTheDocument();
    expect(chip).toHaveAttribute('aria-haspopup', 'dialog');
    expect(chip).toHaveAttribute('aria-expanded', 'false');

    const popover = await openGate();

    expect(chip).toHaveAttribute('aria-expanded', 'true');
    expect(within(popover).getByRole('heading', { name: 'Gate: Blocked' })).toBeInTheDocument();
    expect(within(popover).getByRole('list', { name: 'Why the gate is not OK' })).toHaveTextContent(
      '2 of 4 checked instruments fail (50.0 %), above 5 %.',
    );
    expect(
      within(popover).getByText(/2 of 4 checked instruments fail \(the gate blocks the nightly plan above 5 %\)/),
    ).toBeInTheDocument();
  });

  it('is OK when nothing fails, without reasons', async () => {
    const rows = sampleQualityRows.filter((r) => r.status === 'pass');
    renderQuality({ fake: createQualityFake({ rows }) });

    const chip = await gateChip();
    expect(chip).toHaveTextContent('Gate: OK');
    expect(within(chip).getByTestId('CheckCircleOutlinedIcon')).toBeInTheDocument();
    const popover = await openGate();
    expect(within(popover).getByRole('heading', { name: 'Gate: OK' })).toBeInTheDocument();
    expect(within(popover).queryByRole('list', { name: 'Why the gate is not OK' })).not.toBeInTheDocument();
  });

  it('is open with failures when a few fail but not enough to block', async () => {
    const passing = Array.from({ length: 30 }, (_, n) => ({
      ...sampleQualityRows[0]!,
      instrumentId: `00000000-0000-4000-8000-00000000${String(4000 + n)}`,
      symbol: `NSE:S${String(n).padStart(3, '0')}`,
    }));
    const rows = [...passing, sampleQualityRows[2]!];
    renderQuality({ fake: createQualityFake({ rows }) });

    const chip = await gateChip();
    expect(chip).toHaveTextContent('Gate: Open with failures');
    expect(within(chip).getByTestId('WarningAmberOutlinedIcon')).toBeInTheDocument();
    const popover = await openGate();
    expect(within(popover).getByRole('list', { name: 'Why the gate is not OK' })).toHaveTextContent(
      '1 instruments fail and are skipped by the nightly scan.',
    );
  });

  it('is blocked before the first run', async () => {
    renderQuality({ fake: createQualityFake({ runs: [] }) });

    expect(await gateChip()).toHaveTextContent('Gate: Blocked');
    const popover = await openGate();
    expect(within(popover).getByText('No checks have run yet.')).toBeInTheDocument();
  });

  it('closes with Escape and returns the focus to the chip', async () => {
    renderQuality();
    const chip = await gateChip();
    await openGate();

    await userEvent.keyboard('{Escape}');

    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: /^Gate: / })).not.toBeInTheDocument();
    });
    expect(chip).toHaveFocus();
  });
});

describe('Data quality: run history', () => {
  it('opens from the header as a drawer and lists the runs newest first with trigger, counts, score, state and a failure message', async () => {
    const fake = createQualityFake({
      runs: [
        buildQualityRun({
          id: qualityRunId(3),
          trigger: 'manual',
          startedAt: '2026-10-12T16:00:00+05:30',
          instruments: 1,
          instrumentId: sampleQualityRows[2]!.instrumentId,
          passed: 1,
          warned: 0,
          failed: 0,
          score: 100,
        }),
        buildQualityRun({
          id: qualityRunId(2),
          trigger: 'after_job',
          status: 'failed',
          startedAt: '2026-10-11T16:00:00+05:30',
          score: null,
          error: 'The quality step crashed.',
        }),
        buildQualityRun(),
      ],
    });
    renderQuality({ fake });
    await ready();
    expect(screen.queryByRole('list', { name: 'Recent runs' })).not.toBeInTheDocument();
    expect(fake.calls.runs).toBe(0);

    const drawer = await openHistory();

    const list = await within(drawer).findByRole('list', { name: 'Recent runs' });
    const [first, second, third] = within(list).getAllByRole('listitem');
    expect(first).toHaveTextContent('12 Oct 2026, 16:00 IST');
    expect(first).toHaveTextContent('Manual');
    expect(first).toHaveTextContent('One instrument · 1 passed · 0 warnings · 0 failed · score 100');
    expect(second).toHaveTextContent('After update');
    expect(second).toHaveTextContent('Failed');
    expect(second).toHaveTextContent('The quality step crashed.');
    expect(third).toHaveTextContent('Automatic');
    expect(third).toHaveTextContent('Succeeded');
    expect(third).toHaveTextContent('4 instruments · 1 passed · 1 warning · 2 failed · score 88 · 5 sessions');
  });

  it('asks for the last 20 runs and closes with its button', async () => {
    const { fake } = renderQuality();
    await ready();
    const button = screen.getByRole('button', { name: 'Run history' });

    const drawer = await openHistory();
    await within(drawer).findByRole('list', { name: 'Recent runs' });
    expect(within(drawer).getByText('The last 20 runs of the checks, newest first.')).toBeInTheDocument();
    expect(fake.calls.runs).toBe(1);

    await userEvent.click(within(drawer).getByRole('button', { name: 'Close run history' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Run history' })).not.toBeInTheDocument();
    });
    expect(button).toHaveFocus();
  });

  it('shows a loading indicator while the runs load', async () => {
    renderQuality({ handlers: [http.get('/api/data/quality/runs', () => pendingResponse())] });
    await ready();

    const drawer = await openHistory();

    expect(within(drawer).getByText('Loading recent runs')).toBeInTheDocument();
  });

  it('says when nothing has run, and when the runs cannot be loaded (Retry loads them)', async () => {
    const fake = createQualityFake({ runs: [] });
    fake.failures.runs = true;
    renderQuality({ fake });
    await ready();

    const drawer = await openHistory();
    expect(await within(drawer).findByText('The data service is not available.')).toBeInTheDocument();
    fake.failures.runs = false;
    await userEvent.click(within(drawer).getByRole('button', { name: 'Retry' }));
    expect(await within(drawer).findByText('No checks have run yet.')).toBeInTheDocument();
  });
});
