import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Schemas } from '@/shared/api';
import { basketIds, buildBasket } from '@/test/fixtures';
import { pinClock } from '@/test/pinClock';
import {
  allFigures,
  buildEquity,
  buildEvidence,
  buildGroup,
  buildGroups,
  buildWhatWorks,
  nonValue,
  partsOf,
  value,
} from '@/test/reportFixtures';
import { dimensionReasons, renderReports, reportServer } from '@/test/reportScreen';
import { problemResponse, server } from '@/test/server';

pinClock('2026-10-25T10:00:00Z');

const [b1, b2, b3] = [basketIds.myChoice, basketIds.myLongTerm, basketIds.bestStocks];
const cmp = `?view=baskets&cmp=${b1},${b2}`;

const basketGroup = (id: string, over: Partial<Schemas['GroupView']> = {}) =>
  buildGroup({ key: `basket=${id}`, parts: partsOf('basket', id), ...over });
const cell = (basket: string, strategy: string, over: Partial<Schemas['GroupView']> = {}) =>
  buildGroup({
    key: `${basket}|${strategy}`,
    parts: [
      { dimension: 'basket', value: basket },
      { dimension: 'strategy', value: strategy },
    ],
    ...over,
  });
const series = (id: string, total: number) => ({
  ...buildEquity().series[0]!,
  key: `basket=${id}`,
  parts: partsOf('basket', id),
  totalR: value(total),
});

interface Setup {
  groups?: Schemas['GroupView'][];
  works?: Schemas['WhatWorksView'];
  benchmarks?: 'ok' | 'missing';
  equity?: Schemas['EquitySeriesView'][];
}

/** Over the report fixtures: MyChoice is ranked with a positive bound, MyLongTerm is thin. */
function setup(opts: Setup = {}) {
  const requests = reportServer();
  const thin = allFigures(nonValue('insufficient', 'Too few trades.'), {
    key: `basket=${b2}`,
    parts: partsOf('basket', b2),
    n: 12,
  });
  const works = opts.works ?? {
    ...buildWhatWorks({ kind: 'ranked', nonAdditive: true }),
    ranked: [{ rank: 1, lowerBoundPositive: true, group: cell(b1, 'orb', { n: 120 }) }],
    notRanked: [{ reason: 'Only 12 trades; 45 are needed.', group: cell(b2, 'orb', { n: 12 }) }],
  };
  server.use(
    http.get('/api/baskets', () =>
      HttpResponse.json([
        buildBasket(),
        buildBasket({ id: b2, name: 'MyLongTerm' }),
        buildBasket({ id: b3, name: 'Best stocks' }),
      ]),
    ),
    http.get('/api/reports/groups', ({ request }) => {
      requests.push(new URL(request.url));
      const lens = new URL(request.url).searchParams.get('lens') ?? '';
      if (lens === 'regime' || lens === 'account') {
        return problemResponse({ title: dimensionReasons[lens], code: 'reports.dimension_unavailable' }, 422);
      }
      return HttpResponse.json(buildGroups('basket', opts.groups ?? [thin, basketGroup(b1)]));
    }),
    http.get('/api/reports/what-works', ({ request }) => {
      requests.push(new URL(request.url));
      return HttpResponse.json(works);
    }),
    http.get('/api/reports/equity', ({ request }) => {
      requests.push(new URL(request.url));
      return HttpResponse.json({
        ...buildEquity(),
        nonAdditive: true,
        series: opts.equity ?? [
          series(b1, 12),
          { ...series(b2, 0), points: [], totalR: nonValue('insufficient', 'Too few trades.'), n: 12 },
        ],
      });
    }),
    http.get('/api/reports/pivot', ({ request }) => {
      requests.push(new URL(request.url));
      return HttpResponse.json({
        rowDimension: 'basket',
        columnDimension: new URL(request.url).searchParams.get('lens')?.split(',')[1] ?? 'side',
        metric: 'expectancy_r',
        rows: [b1, b2],
        columns: ['BUY', 'SELL'],
        cells: [
          { row: b1, column: 'BUY', n: 60, value: value(0.4) },
          { row: b1, column: 'SELL', n: 3, value: nonValue('insufficient', 'Too few.') },
          { row: b2, column: 'BUY', n: 0, value: nonValue('empty', 'No trades.') },
        ],
        nonAdditive: true,
        evidence: buildEvidence(),
      });
    }),
    http.get('/api/reports/benchmarks', ({ request }) => {
      requests.push(new URL(request.url));
      if (opts.benchmarks === 'missing') return problemResponse({ title: 'Not found', code: 'reports.not_found' }, 404);
      return HttpResponse.json({
        window: { from: '2024-04-01', to: '2026-04-30', source: 'runs', fromSource: 'runs', toSource: 'runs' },
        membersAsOf: '2026-04-30',
        baskets: [
          {
            basketId: b1,
            basketName: 'MyChoice',
            members: 9,
            included: 8,
            buyAndHold: value(14.5),
            excluded: [{ instrumentId: b3, symbol: 'ZED', reason: 'starts_late' }],
          },
          {
            basketId: b2,
            basketName: 'MyLongTerm',
            members: 2,
            included: 2,
            buyAndHold: nonValue('insufficient', 'Fewer than 3 instruments.'),
            excluded: [],
          },
        ],
        index: { instrumentId: b3, symbol: 'NIFTY 50', isDefault: true, return: value(9.25) },
        nonAdditive: true,
        minMembers: 3,
        evidence: {
          basis: 'price return, dividends not included',
          n: 8,
          range: { from: '2024-04-01', to: '2026-04-30', source: 'runs', fromSource: 'runs', toSource: 'runs' },
          limits: [],
          unavailableInputs: [],
        },
      });
    }),
  );
  return requests;
}

describe('Basket comparison', () => {
  it('asks for two baskets and requests nothing for the comparison', async () => {
    const requests = setup();
    renderReports(`?view=baskets&cmp=${b1}`);
    expect(await screen.findByText('Choose at least two baskets to compare')).toBeInTheDocument();
    await screen.findByRole('region', { name: 'What works' }).catch(() => undefined);
    const comparison = requests.filter(
      (u) => /\/(equity|benchmarks|pivot)$/.test(u.pathname) || u.searchParams.get('lens') === 'basket',
    );
    expect(comparison).toHaveLength(0);
  });

  it('compares two baskets: server order, thin basket without figures, non-additive note, no total row', async () => {
    const requests = setup();
    renderReports(cmp);
    const table = await screen.findByRole('table', { name: 'Basket comparison' });
    await within(table).findByText('MyChoice');
    const rows = within(table).getAllByRole('row');
    expect(rows).toHaveLength(3); // header + two baskets, no total
    // The basket with a lower bound comes first even though the thin one was sent first.
    expect(rows[1]).toHaveTextContent('MyChoice');
    expect(rows[1]).toHaveTextContent('Top ranked cell: orb');
    expect(rows[1]).toHaveTextContent('orb (rank 1)');
    expect(rows[2]).toHaveTextContent('MyLongTerm');
    expect(rows[2]).toHaveTextContent('None ranked: Only 12 trades; 45 are needed.');
    // Thin basket: the trade count only, every figure is a word.
    expect(rows[2]).toHaveTextContent('12');
    expect(rows[2]).not.toHaveTextContent('+0.21');
    expect(rows[2]).not.toHaveTextContent('55.8%');
    expect(rows[2]).toHaveTextContent('n 12 of at least 30');
    expect(
      screen.getAllByText(/A trade in several baskets appears under each; totals are not additive/).length,
    ).toBeGreaterThan(0);
    expect(screen.queryByText(/^Total$/)).toBeNull();
    // Every request names the compared baskets.
    expect(
      requests
        .find((u) => u.pathname.endsWith('/groups') && u.searchParams.get('lens') === 'basket')
        ?.searchParams.get('basket'),
    ).toBe(`${b1},${b2}`);
  });

  it('shows buy and hold, the index and the instruments left out', async () => {
    setup();
    renderReports(cmp);
    const table = await screen.findByRole('table', { name: 'Basket comparison' });
    await waitFor(() => {
      expect(table).toHaveTextContent('+14.50 %');
    });
    expect(table).toHaveTextContent('+9.25 %');
    expect(table).toHaveTextContent('n 2 of at least 3');
    expect(await screen.findByText('1 instruments left out (starts_late)')).toBeInTheDocument();
    expect(screen.getByText('MyChoice: ZED, starts_late')).toBeInTheDocument();
  });

  it('keeps the table complete when the benchmarks are missing', async () => {
    setup({ benchmarks: 'missing' });
    renderReports(cmp);
    const table = await screen.findByRole('table', { name: 'Basket comparison' });
    await waitFor(() => {
      expect(within(table).getAllByText('Not available').length).toBeGreaterThanOrEqual(4);
    });
    expect(table).toHaveTextContent('MyChoice');
    expect(table).not.toHaveTextContent('+14.50');
  });

  it('draws a curve per basket with a trade and names the one without', async () => {
    setup();
    renderReports(cmp);
    expect(await screen.findByText(/No curve for MyLongTerm/)).toBeInTheDocument();
    expect(screen.getAllByText(/The curves overlap/).length).toBeGreaterThan(0);
  });

  it('prints strategy per basket as a bar list with the thin basket as a state', async () => {
    setup();
    renderReports(cmp);
    const list = await screen.findByRole('list', { name: 'Expectancy of orb per basket' });
    const items = within(list).getAllByRole('listitem');
    expect(items).toHaveLength(2);
    expect(items[1]).toHaveTextContent('MyLongTerm');
  });

  it('draws the side grid with a masked cell, an empty cell and no figure for either', async () => {
    setup();
    renderReports(cmp);
    const grid = await screen.findByRole('grid', { name: 'Expectancy by basket and side' });
    const ok = within(grid).getByRole('button', { name: 'MyChoice, BUY: +0.40 R, n 60' });
    expect(ok).toBeInTheDocument();
    const masked = within(grid).getByRole('button', { name: 'MyChoice, SELL: insufficient data, n 3' });
    expect(masked).toHaveTextContent('n 3 of at least 30');
    expect(masked).not.toHaveTextContent(/[+-]\d/);
    expect(within(grid).getAllByText('No trades').length).toBeGreaterThanOrEqual(2);
    expect(screen.getByRole('grid', { name: 'Expectancy by basket and style' })).toBeInTheDocument();
  });

  it('opens a side panel for a cell with the P8-11 line', async () => {
    setup();
    const user = userEvent.setup();
    renderReports(cmp);
    const grid = await screen.findByRole('grid', { name: 'Expectancy by basket and side' });
    await user.click(within(grid).getByRole('button', { name: 'MyChoice, BUY: +0.40 R, n 60' }));
    const dialog = await screen.findByRole('dialog', { name: 'Cell MyChoice, BUY' });
    expect(dialog).toHaveTextContent('Trades of this cell: drill-down arrives with P8-11');
  });

  it('refuses a seventh basket', async () => {
    setup();
    const ids = [1, 2, 3, 4, 5, 6, 7].map((i) => `00000000-0000-4000-8000-00000000050${String(i)}`);
    server.use(
      http.get('/api/baskets', () =>
        HttpResponse.json(ids.map((id, i) => buildBasket({ id, name: `Basket ${String(i + 1)}` }))),
      ),
    );
    const user = userEvent.setup();
    renderReports(`?view=baskets&cmp=${ids.slice(0, 6).join(',')}`);
    await user.click(await screen.findByRole('combobox', { name: 'Baskets to compare' }));
    expect(await screen.findByRole('option', { name: 'Basket 7' })).toHaveAttribute('aria-disabled', 'true');
    expect(screen.getByText(/limit 6/)).toBeInTheDocument();
  });

  it('makes at most six requests for the comparison, asking What works once', async () => {
    const requests = setup();
    renderReports(cmp);
    await screen.findByRole('grid', { name: 'Expectancy by basket and style' });
    await screen.findByRole('list', { name: 'Expectancy of orb per basket' });
    const named = requests.filter((u) => u.searchParams.get('basket') === `${b1},${b2}`);
    expect(named.length).toBeLessThanOrEqual(6);
    expect(named.filter((u) => u.pathname.endsWith('/what-works'))).toHaveLength(1);
  });

  it('takes only the first six baskets of a hand-edited address', async () => {
    const requests = setup();
    const ids = [1, 2, 3, 4, 5, 6, 7, 8].map((i) => `00000000-0000-4000-8000-00000000060${String(i)}`);
    renderReports(`?view=baskets&cmp=${ids.join(',')}`);
    await screen.findByRole('table', { name: 'Basket comparison' });
    const groups = requests.find((u) => u.pathname.endsWith('/groups') && u.searchParams.get('lens') === 'basket');
    expect(groups?.searchParams.get('basket')).toBe(ids.slice(0, 6).join(','));
  });

  it.each(['insufficient', 'unavailable', 'not_evaluable', 'empty'] as const)(
    'prints a %s basket figure without a number',
    async (state) => {
      setup({
        groups: [allFigures(nonValue(state, 'Reason.'), { key: `basket=${b1}`, parts: partsOf('basket', b1), n: 12 })],
      });
      renderReports(cmp);
      const table = await screen.findByRole('table', { name: 'Basket comparison' });
      const row = (await within(table).findAllByRole('row'))[1];
      const figures = within(row!).getAllByRole('cell').slice(2, 7);
      for (const figure of figures) expect(figure.textContent).not.toMatch(/[+-]\d|\d\.\d|%/);
    },
  );

  it('says when the compared baskets replace the basket filter', async () => {
    setup();
    renderReports(`${cmp}&basket=${b3}`);
    expect(await screen.findByText(/compared baskets replace the Baskets filter/)).toBeInTheDocument();
  });
});
