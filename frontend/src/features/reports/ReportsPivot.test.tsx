import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Schemas } from '@/shared/api';
import { basketIds, buildBasket } from '@/test/fixtures';
import { pinClock } from '@/test/pinClock';
import { buildEvidence, nonValue, value } from '@/test/reportFixtures';
import { dimensionReasons, renderReports, reportServer } from '@/test/reportScreen';
import { problemResponse, server } from '@/test/server';

pinClock('2026-10-25T10:00:00Z');

const [b1, b2, b3] = [basketIds.myChoice, basketIds.myLongTerm, basketIds.bestStocks];
const strategies = ['ORB', 'VWAP', 'BASE'];

// Rows B1..B3 by ORB, VWAP, BASE: n = 40, 3, 0 / 12, 55, 31 / 0, 0, 90. Values only from 30 trades.
const grid: [string, string, number, Schemas['MetricView']][] = [
  [b1, 'ORB', 40, value(0.4)],
  [b1, 'VWAP', 3, nonValue('insufficient', 'Too few.')],
  [b1, 'BASE', 0, nonValue('empty', 'No trades.')],
  [b2, 'ORB', 12, nonValue('insufficient', 'Too few.')],
  [b2, 'VWAP', 55, value(-0.2)],
  [b2, 'BASE', 31, value(0.1)],
  [b3, 'ORB', 0, nonValue('empty', 'No trades.')],
  [b3, 'VWAP', 0, nonValue('empty', 'No trades.')],
  [b3, 'BASE', 90, value(0.8)],
];

function setup(pivot?: () => Response) {
  const requests = reportServer();
  server.use(
    http.get('/api/baskets', () =>
      HttpResponse.json([
        buildBasket(),
        buildBasket({ id: b2, name: 'MyLongTerm' }),
        buildBasket({ id: b3, name: 'Best stocks' }),
      ]),
    ),
    http.get('/api/reports/pivot', ({ request }) => {
      requests.push(new URL(request.url));
      if (pivot) return pivot();
      return HttpResponse.json({
        rowDimension: 'basket',
        columnDimension: 'strategy',
        metric: 'expectancy_r',
        rows: [b1, b2, b3],
        columns: strategies,
        cells: grid.map(([row, column, n, v]) => ({ row, column, n, value: v })),
        nonAdditive: true,
        evidence: buildEvidence(),
      });
    }),
  );
  return requests;
}

const open = '?view=pivot&rows=basket&columns=strategy';

describe('Pivot builder', () => {
  it('masks thin cells, labels empty ones and prints a digit only for value cells', async () => {
    setup();
    renderReports(open);
    const table = await screen.findByRole('grid', { name: 'Pivot' });
    const cells = await within(table).findAllByRole('button');
    expect(cells).toHaveLength(6); // the three n = 0 cells have no button
    const values = cells.filter((c) => /[+-]\d\.\d\d R/.test(c.textContent));
    expect(values).toHaveLength(4);
    const masked = cells.filter((c) => c.getAttribute('aria-label')?.includes('insufficient data'));
    expect(masked).toHaveLength(2);
    for (const cell of masked) expect(cell.textContent).not.toMatch(/[+-]\d/);
    expect(within(table).getAllByText('No trades')).toHaveLength(3);
    expect(masked[0]).toHaveTextContent('n 3 of at least 30');
    expect(screen.getByText(/Hatched = insufficient data/)).toBeInTheDocument();
    expect(screen.getByText(/Descriptive, not a ranking/)).toBeInTheDocument();
  });

  it('moves through the grid with the arrow keys, Home, End and the Ctrl variants', async () => {
    setup();
    const user = userEvent.setup();
    renderReports(open);
    const table = await screen.findByRole('grid', { name: 'Pivot' });
    const first = await within(table).findByRole('button', { name: /MyChoice, ORB/ });
    first.focus();
    await user.keyboard('{ArrowRight}');
    expect(within(table).getByRole('button', { name: /MyChoice, VWAP/ })).toHaveFocus();
    await user.keyboard('{ArrowDown}');
    expect(within(table).getByRole('button', { name: /MyLongTerm, VWAP/ })).toHaveFocus();
    await user.keyboard('{End}');
    expect(within(table).getByRole('button', { name: /MyLongTerm, BASE/ })).toHaveFocus();
    await user.keyboard('{Home}');
    expect(within(table).getByRole('button', { name: /MyLongTerm, ORB/ })).toHaveFocus();
    await user.keyboard('{Control>}{End}{/Control}');
    expect(within(table).getByRole('button', { name: /Best stocks, BASE/ })).toHaveFocus();
    await user.keyboard('{Control>}{Home}{/Control}');
    expect(first).toHaveFocus();
  });

  it('sends rows and columns as the lens and the metric by its wire name', async () => {
    const requests = setup();
    renderReports('?view=pivot&rows=basket&columns=strategy&metric=total_r');
    await screen.findByRole('grid', { name: 'Pivot' });
    const call = requests.find((u) => u.pathname.endsWith('/pivot'));
    expect(call?.searchParams.get('lens')).toBe('basket,strategy');
    expect(call?.searchParams.get('metric')).toBe('total_r');
  });

  it('offers the whitelist, states why regime and account are not offered, and prevents the same dimension twice', async () => {
    setup();
    const user = userEvent.setup();
    renderReports(open);
    await screen.findByRole('grid', { name: 'Pivot' });
    expect(await screen.findByText(`Regime (not offered): ${dimensionReasons.regime}`)).toBeInTheDocument();
    expect(screen.getByText(/Decisions are plan tickets: see Learning, Decisions/)).toBeInTheDocument();
    await user.click(screen.getByRole('combobox', { name: 'Columns' }));
    const options = within(await screen.findByRole('listbox')).getAllByRole('option');
    expect(options.map((o) => o.textContent)).toEqual([
      'Basket',
      'Instrument',
      'Strategy',
      'Timeframe',
      'Side',
      'Style',
      'Exit',
      'Month',
      'Weekday',
    ]);
    expect(within(await screen.findByRole('listbox')).getByRole('option', { name: 'Basket' })).toHaveAttribute(
      'aria-disabled',
      'true',
    );
    await user.click(within(screen.getByRole('listbox')).getByRole('option', { name: 'Side' }));
    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: 'Columns' })).toHaveTextContent('Side');
    });
  });

  it('shows the server message and a way back when a pivot is too large', async () => {
    setup(() => problemResponse({ title: 'The pivot has more than 2000 cells.', code: 'reports.invalid' }, 422));
    const user = userEvent.setup();
    renderReports('?view=pivot&rows=instrument&columns=month');
    expect(
      await screen.findByText(/The pivot has more than 2000 cells\. Choose a smaller dimension\./),
    ).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /Back to strategy by timeframe/ }));
    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: 'Rows' })).toHaveTextContent('Strategy');
    });
  });

  it('explains a double count and offers to group by exit as well', async () => {
    setup(() =>
      problemResponse(
        {
          title: 'The runs hold the same signals under different exit labels; group by Exit as well.',
          code: 'reports.double_count',
        },
        422,
      ),
    );
    const user = userEvent.setup();
    renderReports(open);
    expect(await screen.findByText(/group by Exit as well/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /Group by exit as well/ }));
    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: 'Columns' })).toHaveTextContent('Exit');
    });
  });

  it('keeps a long instrument grid whole inside its own scrolling card', async () => {
    const rows = Array.from({ length: 45 }, (_, i) => `SYM${String(i)}`);
    setup(() =>
      HttpResponse.json({
        rowDimension: 'instrument',
        columnDimension: 'side',
        metric: 'expectancy_r',
        rows,
        columns: ['BUY'],
        cells: rows.map((row) => ({ row, column: 'BUY', n: 40, value: value(0.1) })),
        nonAdditive: false,
        evidence: buildEvidence(),
      }),
    );
    renderReports('?view=pivot&rows=instrument&columns=side');
    const table = await screen.findByRole('grid', { name: 'Pivot' });
    expect(await within(table).findAllByRole('rowheader')).toHaveLength(45);
  });

  it('maps the lens selector to the groups table and disables the lenses that cannot answer', async () => {
    const requests = setup();
    const user = userEvent.setup();
    renderReports(open);
    await user.click(await screen.findByRole('combobox', { name: 'Lens' }));
    const listbox = await screen.findByRole('listbox');
    expect(
      within(listbox)
        .getAllByRole('option')
        .map((o) => o.textContent),
    ).toEqual(['All trades', 'By basket', 'By instrument', 'By strategy', 'By individual trade', 'By account']);
    expect(within(listbox).getByRole('option', { name: 'By individual trade' })).toHaveAttribute(
      'aria-disabled',
      'true',
    );
    expect(within(listbox).getByRole('option', { name: 'By account' })).toHaveAttribute('aria-disabled', 'true');
    await user.click(within(listbox).getByRole('option', { name: 'By strategy' }));
    await waitFor(() => {
      expect(requests.some((u) => u.pathname.endsWith('/groups') && u.searchParams.get('lens') === 'strategy')).toBe(
        true,
      );
    });
  });

  it('shows the lens selector in the page header and the non-additive note for a basket lens', async () => {
    setup();
    renderReports(`${open}&lens=basket`);
    expect(await screen.findByRole('combobox', { name: 'Lens' })).toBeInTheDocument();
    expect(await screen.findByRole('table', { name: 'Groups by basket' })).toBeInTheDocument();
  });
});
