import { buildRefreshRun } from '@/test/fixtures';
import { activeFilterCount, defaultSort, emptyFilters, parseFilters, parseSort, toParams } from './filters';
import { outcomeText, phaseLabel, summaryLine } from './labels';

const parse = (query: string) => parseFilters(new URLSearchParams(query));

describe('filters in the URL', () => {
  it('reads defaults from an empty query', () => {
    expect(parse('')).toEqual(emptyFilters);
  });

  it('reads every parameter', () => {
    expect(
      parse('tab=tracked&q=tcs&type=etf&sector=Banking&series=EQ&fno=1&asm=1&retired=1&untracked=1&sort=-lot'),
    ).toEqual({
      tab: 'tracked',
      q: 'tcs',
      type: 'etf',
      sector: 'Banking',
      series: 'EQ',
      fno: true,
      asmGsm: true,
      includeRetired: true,
      untracked: true,
      sort: { field: 'lot', descending: true },
    });
  });

  it('ignores unknown or malformed values', () => {
    expect(parse('type=bond&sort=colour&fno=yes&sector=')).toEqual(emptyFilters);
  });

  it('writes only what differs from the default and round-trips', () => {
    expect(toParams(emptyFilters).toString()).toBe('');
    const filters = parse('q=tcs&type=share&sort=-name&fno=1');
    expect(parse(toParams(filters).toString())).toEqual(filters);
    expect(toParams({ ...emptyFilters, q: '  tcs  ' }).get('q')).toBe('tcs');
  });

  it('keeps unrelated parameters (such as the open instrument)', () => {
    const next = toParams({ ...emptyFilters, type: 'etf' }, new URLSearchParams('id=abc&type=share'));
    expect(next.get('id')).toBe('abc');
    expect(next.get('type')).toBe('etf');
  });

  it('drops the page and page size of an old link: the list loads as the owner scrolls', () => {
    expect(parse('type=etf&page=3&size=100')).toEqual({ ...emptyFilters, type: 'etf' });
    const next = toParams({ ...emptyFilters, type: 'etf' }, new URLSearchParams('page=3&size=100&id=abc'));
    expect(next.toString()).toBe('id=abc&type=etf');
  });

  it('reads a descending sort and ignores unknown columns', () => {
    expect(parseSort('-tick')).toEqual({ field: 'tick', descending: true });
    expect(parseSort('bogus')).toEqual(defaultSort);
  });

  it('counts the narrowing filters', () => {
    expect(activeFilterCount(emptyFilters)).toBe(0);
    expect(activeFilterCount({ ...emptyFilters, q: ' ', sort: { field: 'lot', descending: true } })).toBe(0);
    expect(activeFilterCount({ ...emptyFilters, q: 'a', type: 'etf', fno: true })).toBe(3);
  });
});

describe('refresh wording', () => {
  it('summarises the changes of a run', () => {
    expect(summaryLine(buildRefreshRun())).toBe('+12 added · 3 updated · 5 retired · 4,381 active');
    expect(summaryLine(buildRefreshRun({ added: 0, updated: 0, retired: 0 }))).toBe('No changes · 4,381 active');
  });

  it('words the outcome of each kind of run', () => {
    expect(outcomeText(buildRefreshRun({ status: 'unchanged' }))).toBe('No changes — the file is unchanged.');
    expect(outcomeText(buildRefreshRun({ status: 'failed' }))).toMatch(/failed/);
    expect(outcomeText(buildRefreshRun())).toMatch(/^Instrument list refreshed: \+12 added/);
  });

  it('names the phases and falls back for unknown ones', () => {
    expect(phaseLabel('saving')).toBe('Saving the list');
    expect(phaseLabel('mystery')).toBe('mystery');
    expect(phaseLabel(null)).toBe('Starting');
  });
});
