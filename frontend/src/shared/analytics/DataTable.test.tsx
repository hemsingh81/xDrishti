import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderKit, usesGoodColour } from '@/test/analyticsKit';
import { DataTable, type Column, type TableStatus } from './DataTable';
import { formatR, formatRate } from './format';
import type { MetricState } from './metric';

interface Row {
  id: string;
  cell: string;
  expectancy: MetricState;
  winRate: MetricState;
  n: number | null;
}

const rows: Row[] = [
  {
    id: 'a',
    cell: 'Breakout 15m long',
    expectancy: { kind: 'value', value: 0.42 },
    winRate: { kind: 'value', value: 0.55 },
    n: 128,
  },
  {
    id: 'b',
    cell: 'Pullback 1h long',
    expectancy: { kind: 'insufficient', n: 9, min: 30 },
    winRate: { kind: 'insufficient', n: 9, min: 30 },
    n: 9,
  },
  {
    id: 'c',
    cell: 'Gap 5m short',
    expectancy: { kind: 'unavailable', reason: 'Exit not recorded for run 7' },
    winRate: { kind: 'not_evaluable', reason: 'Needs six folds' },
    n: null,
  },
];

const columns: Column<Row>[] = [
  { kind: 'text', id: 'cell', header: 'Cell', render: (r) => r.cell },
  { kind: 'metric', id: 'exp', header: 'Expectancy', metric: (r) => r.expectancy, format: formatR, n: (r) => r.n },
  { kind: 'metric', id: 'win', header: 'Win rate', metric: (r) => r.winRate, format: formatRate },
];

const evidence = {
  n: 137,
  range: { from: '2024-04-01', to: '2026-04-30' },
  basis: 'gross',
  limits: ['In-sample, modelled fills'],
} as const;

function table(status?: TableStatus, data: Row[] = rows) {
  return (
    <DataTable
      label="What works"
      columns={columns}
      rows={data}
      rowKey={(r) => r.id}
      status={status}
      evidence={evidence}
    />
  );
}

describe('DataTable', () => {
  it('is a named table with column headers and a row header per row', () => {
    renderKit(table());
    const grid = screen.getByRole('table', { name: 'What works' });
    expect(
      within(grid)
        .getAllByRole('columnheader')
        .map((h) => h.textContent),
    ).toEqual(['Cell', 'Expectancy', 'Win rate']);
    expect(within(grid).getByRole('rowheader', { name: 'Breakout 15m long' })).toBeInTheDocument();
  });

  it('draws each cell in its state: a value as a number, a non-value as its word', () => {
    renderKit(table());
    const [, ...bodyRows] = screen.getAllByRole('row');
    const first = within(bodyRows[0]!).getAllByRole('cell');
    expect(first.map((c) => c.textContent)).toEqual(['+0.42 Rn 128', '55.0%']);

    const thin = within(bodyRows[1]!).getAllByRole('cell');
    expect(thin[0]).toHaveTextContent('Insufficient data');
    expect(thin[0]).not.toHaveTextContent('%');

    const missing = within(bodyRows[2]!).getAllByRole('cell');
    expect(missing[0]).toHaveTextContent('Not available');
    expect(missing[0]).toHaveTextContent('Exit not recorded for run 7');
    expect(missing[1]).toHaveTextContent('Not evaluable');
    expect(missing[1]).toHaveTextContent('Needs six folds');
    expect(missing[0]).not.toHaveTextContent(/R\b/);
  });

  it('keeps the thin and missing rows in the table instead of hiding them', () => {
    renderKit(table());
    expect(screen.getAllByRole('row')).toHaveLength(4);
  });

  it('never draws a non-value in the good colour', () => {
    renderKit(table(undefined, rows.slice(1)));
    expect(usesGoodColour(screen.getByRole('table'))).toBe(false);
  });

  it('sits in a labelled region that a keyboard can scroll', async () => {
    renderKit(table());
    const region = screen.getByRole('region', { name: 'What works, scrolls sideways' });
    await userEvent.tab();
    expect(region).toHaveFocus();
  });

  it('prints the evidence under the table', () => {
    renderKit(table());
    const evidenceBlock = screen.getByRole('region', { name: 'Evidence' });
    expect(within(evidenceBlock).getByText('n 137')).toBeInTheDocument();
    expect(within(evidenceBlock).getByText('Gross R (before costs)')).toBeInTheDocument();
    expect(within(evidenceBlock).getByText('In-sample, modelled fills')).toBeInTheDocument();
  });

  it('says there are no records instead of showing an empty table', () => {
    renderKit(table(undefined, []));
    const grid = screen.getByRole('table');
    expect(within(grid).getByText('No records yet')).toBeInTheDocument();
    expect(within(grid).getAllByRole('row')).toHaveLength(2);
    expect(grid).not.toHaveTextContent(/\d/);
  });

  it('shows loading in words and marks the table busy', () => {
    renderKit(table({ kind: 'loading' }));
    const grid = screen.getByRole('table');
    expect(grid).toHaveAttribute('aria-busy', 'true');
    expect(within(grid).getByText('Loading')).toBeInTheDocument();
    expect(within(grid).queryByText('Breakout 15m long')).not.toBeInTheDocument();
  });

  it('shows an error with its reason and keeps the rows out of sight', () => {
    renderKit(table({ kind: 'error', reason: 'The server did not answer' }));
    const grid = screen.getByRole('table');
    expect(grid).toHaveAttribute('aria-busy', 'false');
    expect(within(grid).getByText('Could not load')).toBeInTheDocument();
    expect(within(grid).getByText('The server did not answer')).toBeInTheDocument();
    expect(within(grid).queryByText('Breakout 15m long')).not.toBeInTheDocument();
  });
});
