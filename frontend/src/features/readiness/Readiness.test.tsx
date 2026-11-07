import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildCard, buildList, buildSummary, mixedRows, mountReadiness, scoredSubs } from '@/test/readinessFixtures';
import { server } from '@/test/server';
import { renderReadiness } from './testing';

afterEach(() => {
  server.events.removeAllListeners();
});

describe('Readiness: today, every input unavailable', () => {
  it('says plainly that every score reads 0 and Gate R cannot pass, and shows no digit for an unavailable input', async () => {
    mountReadiness();
    renderReadiness();

    expect(await screen.findByText(/Every score reads 0 today and no strategy can pass Gate R/)).toBeInTheDocument();
    expect(screen.getByText(/not a defect/)).toBeInTheDocument();

    const system = screen.getByRole('region', { name: 'System readiness' });
    expect(within(system).getByText(/equal weights: the plan exposes no risk share/)).toBeInTheDocument();
    expect(within(system).getByText('The integrity checks are not verified.')).toBeInTheDocument();

    const checklist = await screen.findByRole('table', { name: 'The 15-row edge checklist' });
    const rows = within(checklist).getAllByRole('row');
    expect(rows).toHaveLength(16);
    // Every row says "Not available" with the server's reason, counts as a fail and is never a pass.
    expect(within(checklist).getAllByText('Not available (counts as a fail)')).toHaveLength(15);
    expect(within(checklist).queryByText('Pass')).toBeNull();
    expect(within(checklist).getAllByText('Not available')).toHaveLength(15);
    expect(within(checklist).getByText('no producer yet for row 13')).toBeInTheDocument();

    const subs = screen.getByRole('table', { name: 'The six sub-scores' });
    expect(within(subs).getAllByText('Not available (a hard fail)')).toHaveLength(6);
    expect(within(subs).getByText('Power has no input yet')).toBeInTheDocument();
    // No bar and no zero for a sub-score that was not measured.
    expect(within(subs).queryByRole('progressbar')).toBeNull();
    expect(within(subs).queryByText(/^0\.0$/)).toBeNull();

    expect(screen.getByText(/15 of 15 checklist rows are not available/)).toBeInTheDocument();
    const gates = screen.getByRole('region', { name: 'Gates R and S' });
    expect(within(gates).getByText('Gate R: does not pass')).toBeInTheDocument();
    expect(within(gates).getByText('Gate S: does not pass')).toBeInTheDocument();
    expect(within(gates).getAllByText(/Row 10, Concentration/)).toHaveLength(2);

    const waiting = screen.getByRole('list', { name: 'Producers this card is waiting for' });
    expect(within(waiting).getByText('row 13: the benchmark (P8-07)')).toBeInTheDocument();

    const history = screen.getByRole('table', { name: 'Daily snapshots, newest first' });
    expect(within(history).getByText('#41')).toBeInTheDocument();
    expect(within(history).getByText('2026-11-05')).toBeInTheDocument();
    expect(screen.getAllByText('abc123hash').length).toBeGreaterThan(0);
  });

  it('reads the weakest strategy first and opens its scorecard', async () => {
    const seen = mountReadiness();
    renderReadiness();
    await screen.findByRole('table', { name: 'Strategy readiness' });
    expect(seen.cardRequests).toEqual(['orb@3']);
    expect(await screen.findByText('Scorecard: orb v3')).toBeInTheDocument();
  });
});

describe('Readiness: a card with passing rows', () => {
  it('shows the minimum, the weakest link, passes and fails as words and the bars', async () => {
    mountReadiness({
      list: buildList({
        strategies: [buildSummary({ score: 20.2, label: 'not_ready', rowsPassed: 4, weakestSubScore: 'Power' })],
      }),
      cards: {
        'orb@3': buildCard({
          score: 20.2,
          rawMinimum: 20.2,
          subScores: scoredSubs(),
          hardFails: [],
          rows: mixedRows(),
          rowsPassed: 4,
          weakestSubScore: 'Power',
          whatWouldMoveIt: 'Power 38.08 %: 12 more effective trades reach 50',
        }),
      },
    });
    renderReadiness();

    const card = await screen.findByRole('region', { name: 'Scorecard: orb v3' });
    // The score and the minimum before hard fails.
    expect(within(card).getAllByText('20.2')).toHaveLength(2);
    expect(within(card).getByText('Not ready')).toBeInTheDocument();
    expect(within(card).getByText('Power')).toBeInTheDocument();
    expect(within(card).getByText('4 of 15')).toBeInTheDocument();
    expect(within(card).getByText('None')).toBeInTheDocument();

    const subs = screen.getByRole('table', { name: 'The six sub-scores' });
    expect(within(subs).getByRole('progressbar', { name: 'Power: 20.2 of 100' })).toBeInTheDocument();
    expect(within(subs).getAllByRole('progressbar')).toHaveLength(6);
    expect(within(subs).getByText('60.7')).toBeInTheDocument();

    const checklist = screen.getByRole('table', { name: 'The 15-row edge checklist' });
    expect(within(checklist).getAllByText('Pass')).toHaveLength(4);
    expect(within(checklist).getByText('Fail')).toBeInTheDocument();
    expect(within(checklist).getAllByText('Not available (counts as a fail)')).toHaveLength(10);
    expect(within(checklist).getByText('41 % of months positive')).toBeInTheDocument();
    // The alert about missing inputs still shows: ten rows are unavailable.
    expect(screen.getByText(/10 of 15 checklist rows are not available/)).toBeInTheDocument();
  });

  it('says an unknown sub-score state is not a pass and draws no bar for it', async () => {
    const [first, ...rest] = scoredSubs();
    mountReadiness({
      cards: { 'orb@3': buildCard({ subScores: [{ ...first!, state: 'half_known', score: 50 }, ...rest] }) },
    });
    renderReadiness();
    const subs = await screen.findByRole('table', { name: 'The six sub-scores' });
    expect(within(subs).getByText('half known (not a pass)')).toBeInTheDocument();
    expect(within(subs).getAllByRole('progressbar')).toHaveLength(5);
  });

  it('does not print a non-finite score as a number', async () => {
    mountReadiness({ cards: { 'orb@3': buildCard({ score: Number.NaN, rawMinimum: Number.NaN }) } });
    renderReadiness();
    const card = await screen.findByRole('region', { name: 'Scorecard: orb v3' });
    expect(within(card).queryByText(/NaN/)).toBeNull();
    expect(within(card).getAllByText('Not available').length).toBeGreaterThan(0);
  });
});

describe('Readiness: system view and strategy table', () => {
  it('shows the system card, sorts the table and opens another strategy from its link', async () => {
    const seen = mountReadiness({
      list: buildList({
        strategies: [
          buildSummary({ strategy: 'orb', version: 3, score: 10, rowsPassed: 2 }),
          buildSummary({
            strategy: 'vwap',
            version: 1,
            score: 60,
            rowsPassed: 9,
            label: 'building',
            labelText: 'Building',
          }),
        ],
      }),
      cards: { 'orb@3': buildCard(), 'vwap@1': buildCard({ strategy: 'vwap', version: 1, score: 60 }) },
    });
    renderReadiness();

    const table = await screen.findByRole('table', { name: 'Strategy readiness' });
    const names = () =>
      within(table)
        .getAllByRole('rowheader')
        .map((cell) => cell.textContent);
    expect(names()).toEqual(['orb v3', 'vwap v1']);
    await userEvent.selectOptions(screen.getByLabelText('Sort by'), 'Strongest first');
    expect(names()).toEqual(['vwap v1', 'orb v3']);
    expect(within(table).getByText('Building')).toBeInTheDocument();
    expect(within(table).getByText('9 of 15')).toBeInTheDocument();

    await userEvent.click(within(table).getByRole('link', { name: 'vwap v1' }));
    expect(await screen.findByText('Scorecard: vwap v1')).toBeInTheDocument();
    expect(seen.cardRequests).toContain('vwap@1');
  });

  it('picks a strategy from the picker', async () => {
    const seen = mountReadiness({
      cards: { 'orb@3': buildCard(), 'vwap@1': buildCard({ strategy: 'vwap', version: 1 }) },
    });
    renderReadiness();
    await screen.findByText('Scorecard: orb v3');
    await userEvent.selectOptions(screen.getByLabelText('Strategy'), 'vwap');
    expect(await screen.findByText('Scorecard: vwap v1')).toBeInTheDocument();
    expect(seen.cardRequests).toContain('vwap@1');
  });

  it('says so when no strategy is in the Lab, and when a strategy has no Lab version', async () => {
    mountReadiness({ list: buildList({ strategies: [] }) });
    renderReadiness();
    expect(await screen.findAllByText(/No strategy in the Lab yet/)).not.toHaveLength(0);
    expect(screen.getByText(/No strategy with a Lab version/)).toBeInTheDocument();
  });

  it('does not open a scorecard for a strategy without a version', async () => {
    const seen = mountReadiness({
      list: buildList({ strategies: [buildSummary({ strategy: 'raw', version: null })] }),
    });
    renderReadiness('?strategy=raw');
    expect(await screen.findByText(/raw has no Lab version/)).toBeInTheDocument();
    expect(seen.cardRequests).toEqual([]);
  });
});

describe('Readiness: errors', () => {
  it('shows the server refusal of a strategy it does not know', async () => {
    mountReadiness();
    renderReadiness('?strategy=ghost&version=2');
    expect(await screen.findByText('No scorecard for this strategy.')).toBeInTheDocument();
    expect(screen.queryByRole('table', { name: 'The 15-row edge checklist' })).toBeNull();
  });

  it('shows an error for the list and keeps the enforce switch', async () => {
    mountReadiness();
    server.use(http.get('/api/confidence/scorecards', () => new HttpResponse(null, { status: 500 })));
    renderReadiness();
    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: 'Turn enforce ON' })).toBeInTheDocument();
  });

  it('shows a loading state first', () => {
    mountReadiness();
    renderReadiness();
    expect(screen.getAllByRole('status').length).toBeGreaterThan(0);
  });
});
