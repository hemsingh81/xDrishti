import { cellText, evidenceProps, metricCellOf, tableOf } from './toolRows';

describe('toolRows', () => {
  it('makes a metric column of metric-shaped cells and never a digit for a non-value', () => {
    const table = tableOf({
      rows: [
        { basket: 'A', e: { state: 'value', number: 0.1, reason: null } },
        { basket: 'B', e: { state: 'unavailable', number: 7, reason: 'No fills' } },
      ],
    });
    expect(table.columns).toEqual([
      { id: 'basket', metric: false },
      { id: 'e', metric: true },
    ]);
    expect(metricCellOf(table.rows[1]?.e)).toEqual({ kind: 'unavailable', reason: 'No fills' });
    expect(metricCellOf({ state: 'value', number: null })).toMatchObject({ kind: 'unavailable' });
    expect(metricCellOf(null)).toMatchObject({ kind: 'unavailable' });
  });

  it('shows nested objects as inert text', () => {
    expect(cellText({ a: '<b>x</b>' })).toBe('{"a":"<b>x</b>"}');
    expect(tableOf({ rows: [1, 'x'] }).rows).toEqual([{ value: 1 }, { value: 'x' }]);
    expect(tableOf(null).rows).toEqual([]);
  });

  it('reads the evidence', () => {
    expect(evidenceProps({ basis: 'gross', n: 3, dateRange: '2025-01-01..2025-02-01' })).toMatchObject({
      n: 3,
      basis: 'gross',
      range: { from: '2025-01-01', to: '2025-02-01' },
    });
    expect(evidenceProps(null)).toBeNull();
  });
});
