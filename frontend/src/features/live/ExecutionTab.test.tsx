import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Schemas } from '@/shared/api';
import { pinClock } from '@/test/pinClock';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { LivePage } from './components/LivePage';

pinClock('2026-11-07T04:30:00Z');

type Slippage = Schemas['LiveSlippageView'];
type SlippageRow = Schemas['LiveSlippageRowView'];
type Capacity = Schemas['LiveCapacityView'];
type CapacityRow = Schemas['LiveCapacityRowView'];
type Page = Schemas['LiveExecutionsPage'];
type Execution = Schemas['LiveExecutionView'];
type Waterfall = Schemas['LiveWaterfallView'];
type Metric = Schemas['MetricView'];

const value = (number: number): Metric => ({ state: 'value', number, reason: null });
const none = (state: string, reason: string): Metric => ({ state, number: null, reason });

const evidence = {
  basis: 'total slippage in basis points of the entry, by liquidity bucket',
  n: 31,
  firstDate: '2026-10-12',
  lastDate: '2026-11-06',
  referenceKinds: ['ticket_level', 'plan_grade', 'unavailable'],
  bucketEdgesCr: [10, 50, 150, 500],
  limits: ['Arrival is the open of a 1-minute bar, not a quote.'],
};

const row = (over: Partial<SlippageRow> = {}): SlippageRow => ({
  bucket: 'L1',
  side: 'buy',
  style: 'intraday',
  referenceKind: 'ticket_level',
  n: 12,
  mean: value(2.65),
  meanLo: value(1.13),
  meanHi: value(4.17),
  median: value(2.75),
  medianLo: value(0.5),
  medianHi: value(5),
  meanTicks: value(5.3),
  meanR: value(0.0265),
  state: 'value',
  proposal: null,
  ...over,
});

const insufficient = (over: Partial<SlippageRow> = {}) =>
  row({
    bucket: 'L2',
    n: 9,
    mean: none('insufficient', 'n = 9, at least 10 needed'),
    meanLo: none('insufficient', 'n = 9, at least 10 needed'),
    meanHi: none('insufficient', 'n = 9, at least 10 needed'),
    median: none('insufficient', 'n = 9, at least 10 needed'),
    medianLo: none('insufficient', 'n = 9, at least 10 needed'),
    medianHi: none('insufficient', 'n = 9, at least 10 needed'),
    meanTicks: none('insufficient', 'n = 9, at least 10 needed'),
    meanR: none('insufficient', 'n = 9, at least 10 needed'),
    state: 'insufficient',
    ...over,
  });

const slippage = (rows: SlippageRow[]): Slippage => ({
  weekStart: '2026-11-02',
  state: rows.length === 0 ? 'empty' : 'value',
  rows,
  evidence,
});

const capacityRow = (over: Partial<CapacityRow> = {}): CapacityRow => ({
  scope: 'account',
  key: 'all',
  nOrders: 20,
  nFull: 18,
  nPartial: 0,
  nRejected: 1,
  nQueueMiss: 1,
  nPriceMiss: 3,
  nUnclassified: 1,
  score: value(0.8636),
  lo: value(0.667),
  hi: value(0.972),
  state: 'value',
  ...over,
});

const capacity = (rows: CapacityRow[]): Capacity => ({
  weekStart: '2026-11-02',
  state: rows.length === 0 ? 'empty' : 'value',
  rows,
  evidence,
});

const execution = (over: Partial<Execution> = {}): Execution => ({
  id: 7,
  accountId: '0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a61',
  itemId: 1,
  date: '2026-11-03',
  symbol: 'RELIANCE',
  side: 'buy',
  style: 'delivery',
  product: 'CNC',
  bucket: 'L1',
  referenceKind: 'ticket_level',
  fillQty: 40,
  fillVwap: 1250.475,
  referencePrice: 1250,
  arrivalPrice: 1250.2,
  participationPct: 0.000782,
  entryDelaySeconds: 70,
  totalRupees: value(0.475),
  delayRupees: value(0.2),
  executionRupees: value(0.275),
  totalTicks: value(9.5),
  totalBps: value(3.8),
  totalR: value(0.0475),
  delayR: value(0.02),
  executionR: value(0.0275),
  waterfallAvailable: true,
  ...over,
});

const page = (items: Execution[]): Page => ({ items, page: 1, size: 50, total: items.length, evidence });

const waterfall: Waterfall = {
  entryId: 7,
  symbol: 'RELIANCE',
  date: '2026-11-03',
  state: 'value',
  basis: 'net',
  balanced: true,
  rows: [
    { label: 'Plan result, gross (on plan prices)', op: 'start', r: value(1.4), rupees: 560 },
    { label: 'Entry slippage', op: 'minus', r: value(0.0475), rupees: 19 },
    { label: 'Exit difference', op: 'minus', r: value(0.2), rupees: 80 },
    { label: 'Actual result, gross', op: 'total', r: value(1.1525), rupees: 461 },
    { label: 'Charges (live minus plan)', op: 'minus', r: value(0.096), rupees: 38.4 },
    { label: 'Actual result, net', op: 'total', r: value(1.0565), rupees: 422.6 },
  ],
  notes: [],
};

function mount(parts: { slippage?: Slippage; capacity?: Capacity; page?: Page; failSlippage?: boolean }) {
  server.use(
    http.get('/api/live-trading/slippage', () =>
      parts.failSlippage
        ? HttpResponse.json({ title: 'Broken', status: 500, code: 'x' }, { status: 500 })
        : HttpResponse.json(parts.slippage ?? slippage([])),
    ),
    http.get('/api/live-trading/capacity', () => HttpResponse.json(parts.capacity ?? capacity([]))),
    http.get('/api/live-trading/executions', () => HttpResponse.json(parts.page ?? page([]))),
    http.get('/api/live-trading/waterfall', () => HttpResponse.json(waterfall)),
  );
  renderRoutes([{ path: '/live', element: <LivePage /> }], { initialPath: '/live?tab=execution' });
}

describe('the Execution tab', () => {
  it('says there are no estimates yet when nothing is stored, with the command to run', async () => {
    mount({});

    expect(await screen.findByText(/No estimates yet: run `xd live estimates`/)).toBeInTheDocument();
    expect(screen.getByText('No live entry has been measured yet.')).toBeInTheDocument();
    expect(screen.queryByRole('table', { name: 'Slippage by bucket' })).toBeNull();
  });

  it('prints the bucket edges and reference kinds above the slippage table and shows the mean with its interval as text', async () => {
    mount({ slippage: slippage([row()]) });

    const table = await screen.findByRole('table', { name: 'Slippage by bucket' });
    expect(screen.getByText(/Bucket edges \(average daily turnover, crore\): 10, 50, 150, 500/)).toBeInTheDocument();
    expect(
      screen.getByText(/Reference kinds: ticket level, plan grade, no reference, never pooled/),
    ).toBeInTheDocument();
    const r = within(table).getByRole('row', { name: /L1/ });
    expect(within(r).getByText('2.65 (1.13 to 4.17)')).toBeInTheDocument();
    expect(within(r).getByText('2.75 (0.50 to 5.00)')).toBeInTheDocument();
    expect(within(r).getByText('0.0265')).toBeInTheDocument();
    expect(within(r).getByText('Estimate')).toBeInTheDocument();
  });

  it('shows an insufficient row as "n = 9, at least 10 needed" with the word and never a mean', async () => {
    mount({ slippage: slippage([insufficient()]) });

    const r = within(await screen.findByRole('table', { name: 'Slippage by bucket' })).getByRole('row', { name: /L2/ });
    expect(within(r).getByText('n = 9, at least 10 needed')).toBeInTheDocument();
    expect(within(r).getByText('Insufficient')).toBeInTheDocument();
    expect(within(r).queryByText(/\d\.\d\d \(/)).toBeNull();
  });

  it('keeps the two reference kinds of one bucket as two rows and prints a proposal as text', async () => {
    mount({
      slippage: slippage([
        row({ proposal: 'In L1 (buy, intraday, against ticket_level) the mean total slippage is 2.7 bp.' }),
        row({ referenceKind: 'plan_grade', n: 22 }),
      ]),
    });

    const table = await screen.findByRole('table', { name: 'Slippage by bucket' });
    expect(within(table).getAllByRole('row', { name: /L1/ })).toHaveLength(2);
    expect(within(table).getByText('plan grade')).toBeInTheDocument();
    expect(screen.getByText(/the mean total slippage is 2.7 bp/)).toBeInTheDocument();
    expect(screen.getByText(/nothing reads them automatically/)).toBeInTheDocument();
  });

  it('shows the capacity share with its interval, and an insufficient bucket as words with no score', async () => {
    mount({
      slippage: slippage([row()]),
      capacity: capacity([
        capacityRow(),
        capacityRow({
          scope: 'bucket',
          key: 'L3',
          nOrders: 5,
          nFull: 3,
          nPriceMiss: 0,
          nUnclassified: 0,
          score: none('insufficient', 'n = 5, at least 10 needed'),
          lo: none('insufficient', 'n = 5, at least 10 needed'),
          hi: none('insufficient', 'n = 5, at least 10 needed'),
          state: 'insufficient',
        }),
      ]),
    });

    const table = await screen.findByRole('table', { name: 'Capacity by scope' });
    expect(
      within(within(table).getByRole('row', { name: /Account/ })).getByText('0.8636 (0.667 to 0.972)'),
    ).toBeInTheDocument();
    const bucket = within(table).getByRole('row', { name: /L3/ });
    expect(within(bucket).getByText('n = 5, at least 10 needed')).toBeInTheDocument();
    expect(within(bucket).queryByText(/0\.\d{4}/)).toBeNull();
    expect(screen.getByText(/Price miss \(not counted\)/)).toBeInTheDocument();
  });

  it('lists the measured entries with the split and opens the waterfall with its words', async () => {
    mount({ slippage: slippage([row()]), page: page([execution()]) });
    const user = userEvent.setup();

    const r = within(await screen.findByRole('table', { name: 'Measured entries table' })).getByRole('row', {
      name: /RELIANCE/,
    });
    expect(within(r).getByText('3.80')).toBeInTheDocument();
    expect(within(r).getByText('9.5')).toBeInTheDocument();
    expect(within(r).getByText('0.0475')).toBeInTheDocument();
    await user.click(within(r).getByRole('button', { name: 'Show waterfall of RELIANCE' }));

    const rows = await screen.findByRole('table', { name: 'Waterfall rows' });
    expect(within(rows).getByRole('row', { name: /Plan result, gross.*start.*1\.4000/ })).toBeInTheDocument();
    expect(within(rows).getByRole('row', { name: /Entry slippage.*minus.*0\.0475/ })).toBeInTheDocument();
    expect(within(rows).getByRole('row', { name: /Actual result, net.*equals.*1\.0565.*422\.60/ })).toBeInTheDocument();
    expect(screen.getByText('Result is net; the lines add up exactly.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Hide waterfall of RELIANCE' }));
    await waitFor(() => {
      expect(screen.queryByRole('table', { name: 'Waterfall rows' })).toBeNull();
    });
  });

  it('never renders a digit for a figure that is not a value, and says why a trip has no waterfall', async () => {
    mount({
      slippage: slippage([row()]),
      page: page([
        execution({
          referenceKind: 'unavailable',
          referencePrice: null,
          totalRupees: none('unavailable', 'no reference'),
          totalBps: none('unavailable', 'no reference'),
          totalTicks: none('unavailable', 'no reference'),
          totalR: none('unavailable', 'no reference'),
          delayR: none('unavailable', 'no reference'),
          executionR: none('unavailable', 'no reference'),
          waterfallAvailable: false,
        }),
      ]),
    });

    const r = within(await screen.findByRole('table', { name: 'Measured entries table' })).getByRole('row', {
      name: /RELIANCE/,
    });
    expect(within(r).getAllByText('unavailable')).toHaveLength(5);
    expect(within(r).getByText('no reference')).toBeInTheDocument();
    expect(within(r).getByText('not closed in the session')).toBeInTheDocument();
    expect(within(r).queryByRole('button')).toBeNull();
  });

  it('shows the evidence block with n, the sessions and the limits', async () => {
    mount({ slippage: slippage([row()]) });

    const e = await screen.findByRole('region', { name: 'Evidence' });
    expect(within(e).getByText(/n = 31; sessions: 2026-10-12 to 2026-11-06/)).toBeInTheDocument();
    expect(within(e).getByText('Arrival is the open of a 1-minute bar, not a quote.')).toBeInTheDocument();
  });

  it('shows the error state when the estimates cannot be read', async () => {
    mount({ failSlippage: true });

    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });

  it('holds no mutation: the tab imports no mutation hook and posts nothing', () => {
    const sources = import.meta.glob<string>(
      [
        './components/Execution*.tsx',
        './components/Slippage*.tsx',
        './components/Capacity*.tsx',
        './components/Waterfall*.tsx',
        './api/executionQueries.ts',
      ],
      { query: '?raw', import: 'default', eager: true },
    );
    expect(Object.keys(sources).length).toBeGreaterThanOrEqual(8);
    for (const source of Object.values(sources)) {
      expect(source).not.toMatch(/useMutation|api\.(POST|PUT|PATCH|DELETE)/);
    }
  });
});
