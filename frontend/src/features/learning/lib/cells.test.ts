import { describe, expect, it } from 'vitest';
import { buildCell, buildThinCell } from '@/test/learningFixtures';
import { basisOf, buildGrid, cellsEvidence, expectancyOf, lowerBoundOf, statState, timeframeMinutes } from './cells';
import { cellsToRead, countFunnel, maxStateReads } from './funnel';
import { countState } from './overview';

describe('timeframeMinutes', () => {
  it('reads minutes, hours, days and weeks, and sorts an unknown code last', () => {
    expect(['1D', '15m', '1h', '5m', 'weird', '1W'].sort((a, b) => timeframeMinutes(a) - timeframeMinutes(b))).toEqual([
      '5m',
      '15m',
      '1h',
      '1D',
      '1W',
      'weird',
    ]);
  });
});

describe('buildGrid', () => {
  it('makes one row per strategy, side, style, exit and regime, and never merges cells', () => {
    const grid = buildGrid([
      buildCell(),
      buildCell({ timeframe: '5m' }),
      buildCell({ side: 'SELL' }),
      buildCell({ regime: 'up', cellKey: 'orb|15m|BUY|intraday|fixed_r|up' }),
    ]);
    expect(grid.timeframes).toEqual(['5m', '15m']);
    expect(grid.rows.map((row) => row.detail)).toEqual([
      'BUY · intraday · fixed_r',
      'BUY · intraday · fixed_r · up',
      'SELL · intraday · fixed_r',
    ]);
    expect(grid.rows[0]?.cells.size).toBe(2);
  });

  it('is empty for no cells', () => {
    expect(buildGrid([])).toEqual({ timeframes: [], rows: [] });
  });
});

describe('a thin cell has no figure', () => {
  it('is insufficient for every derived statistic, whatever the server stored', () => {
    const thin = buildThinCell();
    expect(expectancyOf(thin)).toEqual({ kind: 'insufficient', n: 12, min: 30 });
    expect(lowerBoundOf(thin)).toEqual({ kind: 'insufficient', n: 12, min: 30 });
    expect(statState(thin, 0.5, 'x')).toEqual({ kind: 'insufficient', n: 12, min: 30 });
  });

  it('is a value for an enough cell, and unavailable (not zero) when the server stored none', () => {
    expect(expectancyOf(buildCell())).toEqual({ kind: 'value', value: 0.31 });
    expect(expectancyOf(buildCell({ expectancyR: null }))).toEqual({
      kind: 'unavailable',
      reason: 'No expectancy is stored for this cell',
    });
    expect(expectancyOf(buildCell({ expectancyR: 0 }))).toEqual({ kind: 'value', value: 0 });
  });
});

describe('evidence of a set of cells', () => {
  it('sums trades, names the snapshots and carries the limits once', () => {
    const evidence = cellsEvidence([buildCell(), buildCell({ timeframe: '5m', n: 8, enough: false })]);
    expect(evidence.n).toBe(150);
    expect(evidence.range).toBeNull();
    expect(evidence.basis).toBe('gross');
    expect(evidence.population).toEqual(['2 cells', 'snapshot cells-2026-10-20, as of 20 Oct 2026']);
    expect(evidence.limits).toHaveLength(1);
  });

  it('does not state a basis that the rows do not agree on, and says so', () => {
    const rows = [buildCell(), buildCell({ timeframe: '5m', basis: 'net R (after charges)' })];
    expect(basisOf(rows)).toBe('unknown');
    expect(cellsEvidence(rows).limits?.at(-1)).toMatch(/mix gross and net/);
    expect(basisOf([buildCell({ basis: 'net R (after charges)' })])).toBe('net');
    expect(basisOf([buildCell({ basis: 'something else' })])).toBe('unknown');
  });
});

describe('countFunnel', () => {
  const read = new Map([
    ['b', { state: 'active', problems: [] }],
    ['c', { state: 'weird', problems: ['x'] }],
  ]);

  it('counts research for cells without rows, the replayed state for the rest, and never places an unread cell', () => {
    expect(countFunnel({ snapshotCells: ['a', 'b', 'c', 'd'], cellsWithRows: ['b', 'c', 'e'], read })).toEqual({
      stages: { research: 2, candidate: 0, paper: 0, active: 1, paused: 0, retired: 0 },
      other: { weird: 1 },
      notRead: 1,
      inconsistent: 1,
    });
  });

  it('reads the newest thirty cells with rows', () => {
    const many = Array.from({ length: 40 }, (_, i) => `c${String(i)}`);
    expect(cellsToRead(many)).toHaveLength(maxStateReads);
    expect(cellsToRead(many)[0]).toBe('c0');
  });
});

describe('countState', () => {
  const section = { counted: 0, truncated: false, split: [], newest: null, champion: null };

  it('is a word for none, provisional when cut, a value otherwise', () => {
    expect(countState(section, 'models')).toEqual({ kind: 'empty', reason: 'No models stored yet' });
    expect(countState({ ...section, counted: 200, truncated: true }, 'models').kind).toBe('provisional');
    expect(countState({ ...section, counted: 3 }, 'models')).toEqual({ kind: 'value', value: 3 });
  });
});
