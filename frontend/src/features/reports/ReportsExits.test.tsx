import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Schemas } from '@/shared/api';
import { buildGroup, buildGroups, nonValue, partsOf, runKeyA, runKeyB, value } from '@/test/reportFixtures';
import { renderReports, reportServer } from '@/test/reportScreen';
import { problemResponse, server } from '@/test/server';

type Comparisons = Schemas['ReportComparisonsView'];
type Selections = Schemas['ReportExitSelectionsView'];
type LabRun = Schemas['LabRunView'];

const comparisons = (name = 'orb'): Comparisons => ({
  comparisons: [
    {
      comparisonKey: 'c'.repeat(64),
      entriesKey: 'e'.repeat(64),
      strategyId: name,
      unit: 'RFilled',
      commonEntries: 120,
      members: [
        { runKey: runKeyA, candidateId: 'FIXED_RR{rr=2}' },
        { runKey: runKeyB, candidateId: 'FIXED_RR{rr=3}' },
      ],
      createdAt: '2026-10-20T04:00:00Z',
    },
  ],
  limits: ['Same entries, different exits: every member enters on the same signals.'],
});

const selections = (over: Partial<Schemas['ExitSelectionView']> = {}): Selections => ({
  selections: [
    {
      selectionKey: 's'.repeat(64),
      comparisonKey: 'c'.repeat(64),
      strategyId: 'orb',
      timeframe: '15m',
      side: 'BUY',
      style: 'intraday',
      regime: '*',
      unit: 'RFilled',
      defaultCandidateId: 'FIXED_RR{rr=2}',
      chosenCandidateId: 'FIXED_RR{rr=3}',
      noChoiceReason: null,
      stable: false,
      stabilityReason: 'only 1 usable fold',
      usableFolds: 4,
      agreeingFolds: 3,
      positiveFolds: 2,
      chancePass: 0.25,
      lonePeaks: 1,
      pooled: [
        { name: 'walk-forward choice', trades: 80, meanR: value(0.2), score: value(0.1) },
        {
          name: 'default FIXED_RR{rr=2}',
          trades: 12,
          meanR: nonValue('insufficient', '12 trades, at least 30 needed'),
          score: nonValue('unavailable', 'no figure'),
        },
      ],
      folds: [
        {
          index: 1,
          testStart: '2025-01-01',
          testEnd: '2025-03-31',
          winner: 'FIXED_RR{rr=3}',
          winnerTestTrades: 40,
          winnerTestScore: value(0.31),
          thin: false,
        },
        {
          index: 2,
          testStart: '2025-04-01',
          testEnd: '2025-06-30',
          winner: null,
          winnerTestTrades: 0,
          winnerTestScore: nonValue('empty', 'no candidate qualified in this fold'),
          thin: true,
        },
      ],
      banner: ['The choice is in-sample on the data shown and is not evidence of an edge.'],
      createdAt: '2026-10-21T04:00:00Z',
      ...over,
    },
  ],
  limits: ['Out of sample means walk-forward folds inside the same replayed history.'],
});

function exitServer(c: Comparisons = comparisons(), s: Selections = selections()) {
  const groupCalls: URL[] = [];
  server.use(
    http.get('/api/reports/comparisons', () => HttpResponse.json(c)),
    http.get('/api/reports/exit-selections', () => HttpResponse.json(s)),
    http.get('/api/reports/groups', ({ request }) => {
      const url = new URL(request.url);
      if (url.searchParams.get('lens') === 'exit') {
        groupCalls.push(url);
        return HttpResponse.json(
          buildGroups('exit', [
            buildGroup({ key: 'a', parts: partsOf('exit', 'FIXED_RR{rr=2}'), n: 120, expectancyR: value(0.1) }),
            buildGroup({ key: 'b', parts: partsOf('exit', 'FIXED_RR{rr=3}'), n: 120, expectancyR: value(0.2) }),
          ]),
        );
      }
      return HttpResponse.json(buildGroups('regime', []));
    }),
  );
  return groupCalls;
}

describe('exit comparison', () => {
  it('chooses a stored comparison and reports its member runs split by exit on the same n', async () => {
    reportServer();
    const calls = exitServer();
    renderReports('?view=exits');
    expect(await screen.findByText('Choose a stored comparison to put its exits side by side.')).toBeInTheDocument();
    expect(calls).toHaveLength(0);

    await userEvent.click(await screen.findByRole('button', { name: /Use the runs of the orb comparison/ }));

    const table = await screen.findByRole('table', { name: 'Exits on identical entries' });
    expect(within(table).getByText('FIXED_RR{rr=2}')).toBeInTheDocument();
    expect(within(table).getByText('FIXED_RR{rr=3}')).toBeInTheDocument();
    expect(screen.getByText(/n is the same for every row/)).toBeInTheDocument();
    const sent = calls.at(-1);
    expect(sent?.searchParams.get('runs')).toBe(`${runKeyA},${runKeyB}`);
    expect(sent?.searchParams.get('lens')).toBe('exit');
  });

  it('does not claim identical entries when n differs', async () => {
    reportServer();
    exitServer();
    server.use(
      http.get('/api/reports/groups', ({ request }) =>
        new URL(request.url).searchParams.get('lens') === 'exit'
          ? HttpResponse.json(
              buildGroups('exit', [
                buildGroup({ key: 'a', parts: partsOf('exit', 'x'), n: 100 }),
                buildGroup({ key: 'b', parts: partsOf('exit', 'y'), n: 90 }),
              ]),
            )
          : HttpResponse.json(buildGroups('regime', [])),
      ),
    );
    renderReports(`?view=exits&runs=${runKeyA},${runKeyB}`);
    expect(await screen.findByText(/n differs between the rows/)).toBeInTheDocument();
    expect(screen.queryByText(/n is the same for every row/)).toBeNull();
  });

  it('says so when no comparison is stored, and prints strategy names as text', async () => {
    reportServer();
    exitServer({ comparisons: [], limits: [] }, { selections: [], limits: [] });
    renderReports('?view=exits');
    expect(await screen.findByText(/No exit comparison is stored/)).toBeInTheDocument();
    expect(await screen.findByText(/No walk-forward exit choice is stored/)).toBeInTheDocument();
  });

  it('does not render markup in a strategy name', async () => {
    reportServer();
    exitServer(comparisons('<img src=x onerror=alert(1)>'));
    renderReports('?view=exits');
    expect(await screen.findByText(/<img src=x onerror=alert\(1\)>: FIXED_RR/)).toBeInTheDocument();
    expect(document.querySelector('img[src="x"]')).toBeNull();
  });

  it('prints the stored out-of-sample choice with non-values as words', async () => {
    reportServer();
    exitServer();
    renderReports('?view=exits');
    expect(
      await screen.findByText(/Walk-forward choice: FIXED_RR\{rr=3\} \(default: FIXED_RR\{rr=2\}\)/),
    ).toBeInTheDocument();
    expect(screen.getByText(/Unstable: only 1 usable fold/)).toBeInTheDocument();
    expect(screen.getByText(/probability 0\.25/)).toBeInTheDocument();
    const pooled = await screen.findByRole('table', { name: /Pooled out-of-sample figures/ });
    const defaultRow = within(pooled).getByText('default FIXED_RR{rr=2}').closest('tr');
    expect(defaultRow).not.toBeNull();
    expect(within(defaultRow as HTMLElement).queryByText(/0\.\d/)).toBeNull();
    const folds = screen.getByRole('table', { name: /Folds,/ });
    expect(within(folds).getByText('None qualified')).toBeInTheDocument();
    expect(screen.getByText(/not evidence of an edge/)).toBeInTheDocument();
  });

  it('says there is no choice and uses the default', async () => {
    reportServer();
    exitServer(
      comparisons(),
      selections({ chosenCandidateId: null, noChoiceReason: 'no stable winner', stable: null, chancePass: null }),
    );
    renderReports('?view=exits');
    expect(await screen.findByText(/No choice: no stable winner/)).toBeInTheDocument();
    expect(screen.getByText(/No stability statement/)).toBeInTheDocument();
  });
});

const labRun = (id: string, state = 'succeeded'): LabRun =>
  ({
    id,
    key: 'k',
    state,
    stage: null,
    strategy: { id: 'MY_ORB', version: 1, hash: 'h' },
    createdAt: '2026-10-20T04:00:00Z',
  }) as unknown as LabRun;

const runId = '00000000-0000-4000-8000-0000000000a1';

describe('Monte Carlo risk', () => {
  const monteCarlo = {
    trades: 85,
    draws: 5000,
    seed: '12345678901234567890',
    observedMeanR: 0.2,
    observedMaxDrawdownR: -6.5,
    meanP05: -0.01,
    meanP50: 0.2,
    meanP95: 0.4,
    drawdownP50: -7,
    drawdownP95: -12,
    shareNotPositive: 0.04,
  };

  it('reads nothing of a run until it is chosen, then shows the table with n, draws and seed as text', async () => {
    reportServer();
    let reads = 0;
    server.use(
      http.get('/api/lab/runs', () => HttpResponse.json([labRun(runId)])),
      http.get('/api/lab/runs/:id/report', () => {
        reads += 1;
        return HttpResponse.json({ robustness: { monteCarlo, limits: ['A bootstrap assumes independent trades.'] } });
      }),
    );
    renderReports('?view=montecarlo');
    const button = await screen.findByRole('button', { name: 'Show Monte Carlo risk' });
    expect(reads).toBe(0);

    await userEvent.click(button);

    expect(
      await screen.findByText(/85 out-of-sample trades, 5,000 draws, seed 12345678901234567890/),
    ).toBeInTheDocument();
    expect(screen.getByRole('table', { name: /Monte Carlo risk/ })).toBeInTheDocument();
    expect(screen.getByText('A bootstrap assumes independent trades.')).toBeInTheDocument();
    expect(reads).toBe(1);
  });

  it('says a run without a robustness block has none', async () => {
    reportServer();
    server.use(
      http.get('/api/lab/runs', () => HttpResponse.json([labRun(runId)])),
      http.get('/api/lab/runs/:id/report', () => HttpResponse.json({ robustness: null })),
    );
    renderReports('?view=montecarlo');
    await userEvent.click(await screen.findByRole('button', { name: 'Show Monte Carlo risk' }));
    expect(await screen.findByText(/stored no Monte Carlo bootstrap/)).toBeInTheDocument();
    expect(screen.queryByRole('table', { name: /Monte Carlo risk/ })).toBeNull();
  });

  it('says a run without a report has none (409 lab.no_report)', async () => {
    reportServer();
    server.use(
      http.get('/api/lab/runs', () => HttpResponse.json([labRun(runId, 'failed')])),
      http.get('/api/lab/runs/:id/report', () =>
        problemResponse({ title: 'The run has no report.', code: 'lab.no_report' }, 409),
      ),
    );
    renderReports('?view=montecarlo');
    await userEvent.click(await screen.findByRole('button', { name: 'Show Monte Carlo risk' }));
    expect(await screen.findByText(/has no report/)).toBeInTheDocument();
  });

  it('says so when there is no Lab run', async () => {
    reportServer();
    server.use(http.get('/api/lab/runs', () => HttpResponse.json([])));
    renderReports('?view=montecarlo');
    expect(await screen.findByText('No Lab run yet.')).toBeInTheDocument();
  });
});

describe('pointer entries', () => {
  it('lists the reports the data cannot serve with the reason, no chart and no figure', async () => {
    reportServer();
    renderReports();
    const nav = await screen.findByRole('navigation', { name: 'Report catalogue' });
    for (const name of [
      'Regime',
      'Calibration',
      'Selector effectiveness',
      'Decisions',
      'Backtest, paper and live',
      'Discipline',
      'Execution gap',
      'Account comparison',
    ]) {
      expect(within(nav).getByText(name)).toBeInTheDocument();
    }
    expect(await within(nav).findByText(/A simulated trade has no account/)).toBeInTheDocument();
    expect(within(nav).getAllByRole('link', { name: 'Open Learning, Decisions' })).toHaveLength(2);
    expect(within(nav).queryByRole('img')).toBeNull();
    expect(nav.querySelector('svg:not([aria-hidden])')).toBeNull();
    expect(within(nav).queryByRole('table')).toBeNull();
    // No figure: strip the slice codes (P8-04, P10 ...), then no digit may remain in the pointer entries.
    const text = nav.textContent.replace(/P\d+(-\d+\w?)?( and P\d+-\d+\w?)?/g, '');
    const pointerText = text.slice(text.indexOf('Regime'));
    expect(pointerText).not.toMatch(/\d/);
  });
});
