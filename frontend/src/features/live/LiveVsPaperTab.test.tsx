import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Schemas } from '@/shared/api';
import { pinClock } from '@/test/pinClock';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { LivePage } from './components/LivePage';

pinClock('2026-11-07T04:30:00Z');

type Report = Schemas['LiveVsPaperView'];
type Gap = Schemas['ExecutionGapView'];
type Metric = Schemas['MetricView'];
type Evidence = Schemas['LiveReportEvidenceView'];

const value = (number: number): Metric => ({ state: 'value', number, reason: null });
const none = (state: string, reason: string): Metric => ({ state, number: null, reason });

const evidence: Evidence = {
  basis: 'gross',
  n: 31,
  firstDate: '2026-10-12',
  lastDate: '2026-11-06',
  referenceKinds: ['ticket_level'],
  paperAccountId: 'b0000000-0000-0000-0000-000000000001',
  paperEpochId: 3,
  pairRule: 'Same plan item on the live account and one paper account of the current epoch.',
  exclusions: [{ reason: 'live_only', count: 2 }],
  limits: ['The paper fill is itself modelled.'],
};

const noLine = { state: 'empty', reason: null, points: [] };
const line = (v: number) => ({ state: 'value', reason: null, points: [{ date: '2026-11-03', value: v, n: 1 }] });

const report = (over: Partial<Report> = {}): Report => ({
  state: 'value',
  summary: {
    nPairs: 31,
    mean: value(0.0412),
    sd: value(0.15),
    se: value(0.027),
    lo95: value(-0.0138),
    hi95: value(0.0962),
    upperOneSided95: value(0.0907),
    lowerOneSided95: value(-0.0083),
    entryDiffMean: value(0.0112),
    exitDiffMean: value(0.03),
    chargesDiffMean: none('unavailable', 'charges are not recorded on both sides for every pair'),
    verdict: 'within',
    threshold: 0.1,
    minPairs: 30,
  },
  pairs: [
    {
      planItemId: 1,
      symbol: 'RELIANCE',
      side: 'buy',
      date: '2026-11-03',
      liveR: 1.1525,
      paperR: 1.27,
      shortfallR: 0.1175,
      entryDiffR: 0.0175,
      exitDiffR: 0.1,
    },
  ],
  pairsTotal: 31,
  page: 1,
  size: 200,
  excluded: [
    {
      planItemId: 2,
      symbol: 'TCS',
      date: '2026-11-03',
      reason: 'live_only',
      detail: 'paper skip code gap_beyond_limit',
    },
  ],
  histogram: {
    kind: 'ticket_level',
    n: 12,
    state: 'value',
    bins: [
      { lo: null, hi: -10, count: 0 },
      { lo: -10, hi: -5, count: 1 },
      { lo: -5, hi: null, count: 11 },
    ],
  },
  equity: { planPrices: line(1.4), paper: line(1.27), live: line(1.1525) },
  evidence,
  ...over,
});

const groupRows = (plan: number): Gap['total']['rows'] => [
  { label: 'Plan R', r: value(plan) },
  { label: 'Missed triggers', r: value(-2) },
  { label: 'Missed (cause unknown)', r: value(0) },
  { label: 'Entry slippage', r: value(-0.0475) },
  { label: 'Exit difference', r: value(-0.2) },
  { label: 'Charges difference', r: none('unavailable', 'charges are not recorded for every round trip of the group') },
  { label: 'Result', r: value(1.1525) },
  { label: 'Overrides', r: none('unavailable', 'measured by the decisions report of P8-04') },
];

const group = (key: string): Gap['total'] => ({
  key,
  n: 3,
  state: 'value',
  basis: 'gross',
  balanced: true,
  excluded: 1,
  rows: groupRows(3.4),
  perTicketMean: none('insufficient', 'n = 3; a per-ticket mean needs 5 tickets'),
});

const gap = (over: Partial<Gap> = {}): Gap => ({
  state: 'value',
  groupBy: 'strategy',
  groups: [group('ORB')],
  total: group('total'),
  excluded: [{ planItemId: 6, symbol: 'SBIN', group: 'ORB', date: '2026-11-03', reason: 'unknown' }],
  evidence,
  ...over,
});

const seen: string[] = [];

function mount(parts: { report?: Report | 'fail' | 'noEvidence'; gap?: Gap }, initial = '/live?tab=vs-paper') {
  seen.length = 0;
  server.use(
    http.get('/api/live-trading/reports/live-vs-paper', ({ request }) => {
      seen.push(new URL(request.url).search);
      if (parts.report === 'fail') {
        return HttpResponse.json({ title: 'Broken', status: 500, code: 'x' }, { status: 500 });
      }
      if (parts.report === 'noEvidence') {
        const body: Partial<Report> = report();
        delete body.evidence;
        return HttpResponse.json(body);
      }
      return HttpResponse.json(parts.report ?? report());
    }),
    http.get('/api/live-trading/reports/execution-gap', ({ request }) => {
      seen.push(new URL(request.url).search);
      return HttpResponse.json(parts.gap ?? gap());
    }),
  );
  renderRoutes([{ path: '/live', element: <LivePage /> }], { initialPath: initial });
}

describe('the Live vs paper tab', () => {
  it('shows the tiles with the interval as text, the verdict as icon and word, and the Evidence block', async () => {
    mount({});

    const summary = await screen.findByRole('region', { name: 'Summary' });
    expect(within(summary).getByText('31')).toBeInTheDocument();
    expect(within(summary).getByText('0.041')).toBeInTheDocument();
    expect(within(summary).getByText('-0.014 to 0.096 R')).toBeInTheDocument();
    expect(within(summary).getByText('Verdict: Within the margin')).toBeInTheDocument();
    expect(within(summary).getByText('unavailable')).toBeInTheDocument(); // the charges part is a word, never a digit
    const e = screen.getByRole('region', { name: 'Evidence' });
    expect(
      within(e).getByText(/Basis: gross. n = 31; sessions: 2026-10-12 to 2026-11-06; paper account b0000000/),
    ).toBeInTheDocument();
    expect(within(e).getByText(/Left out: Live only \(no paper entry\) 2/)).toBeInTheDocument();
    expect(within(e).getByText('The paper fill is itself modelled.')).toBeInTheDocument();
  });

  it('shows no digit for an insufficient mean and says why', async () => {
    mount({
      report: report({
        summary: {
          ...report().summary,
          nPairs: 4,
          mean: none('insufficient', 'n = 4; an interval needs 10 pairs'),
          lo95: none('insufficient', 'n = 4; an interval needs 10 pairs'),
          hi95: none('insufficient', 'n = 4; an interval needs 10 pairs'),
          verdict: 'insufficient',
        },
      }),
    });

    const summary = await screen.findByRole('region', { name: 'Summary' });
    expect(within(summary).getByText('Verdict: Insufficient pairs')).toBeInTheDocument();
    expect(within(summary).getAllByText('insufficient').length).toBeGreaterThanOrEqual(2);
    expect(within(summary).queryByText(/0\.041/)).toBeNull();
  });

  it('lists the pairs and each left-out ticket with its reason', async () => {
    mount({});

    const row = within(await screen.findByRole('table', { name: 'Paired tickets' })).getByRole('row', {
      name: /RELIANCE/,
    });
    expect(within(row).getByText('0.1175')).toBeInTheDocument();
    expect(
      screen.getByText(/TCS 2026-11-03, ticket 2: Live only \(no paper entry\)\. paper skip code gap_beyond_limit/),
    ).toBeInTheDocument();
    expect(screen.getByText('Showing 1 of 31 pairs.')).toBeInTheDocument();
  });

  it('draws the three lines and the histogram, with the histogram as a table fallback', async () => {
    mount({});

    expect(await screen.findByRole('region', { name: 'Cumulative R of the paired set' })).toBeInTheDocument();
    const histogram = screen.getByRole('region', { name: 'Live slippage histogram' });
    expect(within(histogram).getByText('Reference kind: ticket_level; n = 12.')).toBeInTheDocument();
  });

  it('says insufficient for a histogram under 10 observations and unavailable for a missing line, with no flat line', async () => {
    mount({
      report: report({
        histogram: { kind: 'plan_grade', n: 6, state: 'insufficient', bins: [] },
        equity: {
          planPrices: {
            state: 'unavailable',
            reason: 'a pair has no plan prices (the plan was not graded)',
            points: [],
          },
          paper: noLine,
          live: noLine,
        },
      }),
    });

    expect(await screen.findByText('Insufficient: n = 6; a histogram needs 10 observations.')).toBeInTheDocument();
    expect(screen.getByText(/Plan prices: unavailable\. a pair has no plan prices/)).toBeInTheDocument();
    expect(screen.getByText('No line to draw: no closed pair, or an input is unavailable.')).toBeInTheDocument();
  });

  it('says there is no live round trip yet when the report is empty, and unavailable when the paper record is unreadable', async () => {
    mount({ report: report({ state: 'empty', pairs: [], pairsTotal: 0 }) });
    expect(await screen.findByText('No live round trip yet.')).toBeInTheDocument();
  });

  it('says the paper record could not be read, not that nothing happened', async () => {
    mount({ report: report({ state: 'unavailable' }) });
    expect(
      await screen.findByText('The paper record could not be read, so no comparison is shown.'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Summary' })).toBeNull();
    expect(screen.queryByText(/Insufficient pairs/)).toBeNull(); // unavailable is not an under-powered sample
    expect(screen.queryByText('No live round trip yet.')).toBeNull();
  });

  it('refuses a response without its Evidence block', async () => {
    mount({ report: 'noEvidence' });

    expect(await screen.findByText(/no Evidence block, so it is not shown/)).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Summary' })).toBeNull();
  });

  it('shows the error state when the report cannot be read', async () => {
    mount({ report: 'fail' });
    expect(await screen.findAllByRole('alert')).not.toHaveLength(0);
  });

  it('shows the waterfall rows of a group with non-values as words and the mean as insufficient under 5 tickets', async () => {
    mount({});

    const table = await screen.findByRole('table', { name: 'Waterfall ORB' });
    expect(within(table).getByRole('row', { name: /^Plan R 3\.4000/ })).toBeInTheDocument();
    const charges = within(table).getByRole('row', { name: /Charges difference/ });
    expect(within(charges).getByText('unavailable')).toBeInTheDocument();
    expect(within(table).getByRole('row', { name: /Overrides/ })).toHaveTextContent('decisions report of P8-04');
    expect(within(table).getByRole('row', { name: /Mean per ticket/ })).toHaveTextContent('insufficient');
    expect(screen.getByText(/SBIN 2026-11-03, ticket 6: Outcome unknown/)).toBeInTheDocument();
  });

  it('switches the group-by to month through the URL state and asks the API for it', async () => {
    mount({}, '/live?tab=vs-paper&by=month');

    await waitFor(() => {
      expect(seen.some((s) => s.includes('groupBy=month'))).toBe(true);
    });
    expect(await screen.findByRole('heading', { name: 'Plan to actual, by month' })).toBeInTheDocument();
  });

  it('holds no mutation: the tab imports no mutation hook and issues no non-GET call', () => {
    const sources = import.meta.glob<string>(
      ['./components/LiveVsPaper*.tsx', './components/Report*.tsx', './api/reportQueries.ts'],
      {
        query: '?raw',
        import: 'default',
        eager: true,
      },
    );
    expect(Object.keys(sources).length).toBeGreaterThanOrEqual(7);
    for (const source of Object.values(sources)) {
      expect(source).not.toMatch(/useMutation|api\.(POST|PUT|PATCH|DELETE)/);
    }
  });
});
