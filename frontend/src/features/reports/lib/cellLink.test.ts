import { describe, expect, it } from 'vitest';
import { cellHref } from './cellLink';

describe('cellHref', () => {
  it('carries the report filters and one filter per part of the cell', () => {
    const href = cellHref(
      { from: '2026-01-01' },
      [
        { dimension: 'strategy', value: 'ORB' },
        { dimension: 'timeframe', value: '15m' },
      ],
      'ORB, 15m',
    );
    const url = new URL(href ?? '', 'http://x');
    expect(url.pathname).toBe('/reports/cell');
    expect(Object.fromEntries(url.searchParams)).toEqual({
      from: '2026-01-01',
      strategy: 'ORB',
      timeframe: '15m',
      cell: 'ORB, 15m',
    });
  });

  it('has no address for a cell that cannot be said as filters, so no wider list is shown under its name', () => {
    expect(cellHref({}, [{ dimension: 'month', value: '2026-03' }], 'March')).toBeNull();
    expect(cellHref({}, [{ dimension: 'instrument', value: 'X' }], 'X')).toBeNull();
    expect(cellHref({}, [{ dimension: 'strategy', value: null }], 'none')).toBeNull();
  });
});
