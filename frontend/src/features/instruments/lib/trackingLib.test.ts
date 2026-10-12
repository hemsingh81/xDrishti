import { ApiError } from '@/shared/api';
import { trackedInfo, untrackedInfo } from '@/test/fixtures';
import { chunk } from '@/shared/lib/chunks';
import { trackingProblemText } from './errors';
import { activeFilterCount, emptyFilters, parseFilters, selectionScope, toParams } from './filters';
import { parseSymbols, symbolsFromCsv } from './parseSymbols';
import { trackedMessage, unmatchedReason, untrackedMessage } from './trackingText';
import { trackingFacts, trackingStatusText } from './trackingFacts';

describe('pasted symbols', () => {
  it('splits on new lines, commas, semicolons and blanks and drops empty entries', () => {
    expect(parseSymbols('RELIANCE, TCS;INFY\n\n  NSE:HDFCBANK\tINE040A01034 ,')).toEqual([
      'RELIANCE',
      'TCS',
      'INFY',
      'NSE:HDFCBANK',
      'INE040A01034',
    ]);
    expect(parseSymbols('  \n ')).toEqual([]);
  });

  it('takes the symbol column of a CSV with a header, whatever its position', () => {
    expect(symbolsFromCsv('Name,Ticker,Qty\r\n"Reliance, Inds",RELIANCE,10\nTCS Ltd,TCS,5\n')).toEqual([
      'RELIANCE',
      'TCS',
    ]);
    expect(symbolsFromCsv('qty;ISIN\n3;INE040A01034')).toEqual(['INE040A01034']);
  });

  it('takes the first column of a CSV without a header, and handles quotes', () => {
    expect(symbolsFromCsv('RELIANCE,1\n"TCS",2\n\n"A ""quoted"" one",3')).toEqual([
      'RELIANCE',
      'TCS',
      'A "quoted" one',
    ]);
    expect(symbolsFromCsv('')).toEqual([]);
    expect(symbolsFromCsv('symbol\n')).toEqual([]);
  });
});

describe('chunks of ids', () => {
  it('splits into slices of at most 500 by default', () => {
    const ids = Array.from({ length: 1_001 }, (_, n) => String(n));
    expect(chunk(ids).map((slice) => slice.length)).toEqual([500, 500, 1]);
    expect(chunk([])).toEqual([]);
    expect(chunk(['a', 'b', 'c'], 2)).toEqual([['a', 'b'], ['c']]);
  });
});

describe('tracking wording', () => {
  const track = { requested: 12, added: 8, reactivated: 2, alreadyTracked: 2, totalTracked: 20 };

  it('words a track result for one or many instruments', () => {
    expect(trackedMessage({ ...track, requested: 1, added: 1, reactivated: 0, alreadyTracked: 0 }, 'NSE:TCS')).toBe(
      'NSE:TCS tracked',
    );
    expect(trackedMessage({ ...track, requested: 1, added: 0, reactivated: 0, alreadyTracked: 1 }, 'NSE:TCS')).toBe(
      'NSE:TCS is already tracked',
    );
    expect(trackedMessage(track, '12 instruments')).toBe(
      'Tracked 10 instruments (8 new, 2 tracked again, 2 already tracked)',
    );
  });

  it('words an untrack result', () => {
    const base = { requested: 1, untracked: 1, notTracked: 0, totalTracked: 3 };
    expect(untrackedMessage(base, 'NSE:TCS')).toBe('NSE:TCS untracked — its history is kept');
    expect(untrackedMessage({ ...base, untracked: 0, notTracked: 1 }, 'NSE:TCS')).toBe('NSE:TCS was not tracked');
    expect(untrackedMessage({ requested: 5, untracked: 3, notTracked: 2, totalTracked: 1 }, '5 instruments')).toBe(
      'Untracked 3 instruments (2 were not tracked) — their history is kept',
    );
  });

  it('explains why an entry did not match', () => {
    expect(unmatchedReason('not_found')).toMatch(/Not found/);
    expect(unmatchedReason('ambiguous')).toMatch(/share this ISIN/);
    expect(unmatchedReason('invalid')).toMatch(/Not a valid symbol/);
    expect(unmatchedReason('other')).toBe('other');
  });

  it('uses the field message of a refused call, else the title, else a connection hint', () => {
    const retired = new ApiError(400, {
      title: 'Retired instruments cannot be tracked.',
      errors: { instrumentIds: ["Not in Dhan's list any more, so it cannot be tracked: NSE:OLDCO."] },
    });
    expect(trackingProblemText(retired, 'fallback')).toMatch(/NSE:OLDCO/);
    expect(trackingProblemText(new ApiError(409, { title: 'Try again.' }), 'fallback')).toBe('Try again.');
    expect(trackingProblemText(new ApiError(500), 'fallback')).toBe('fallback');
    expect(trackingProblemText(new TypeError('network'), 'fallback')).toMatch(/Could not reach the server/);
  });
});

describe('tracking facts of the drawer', () => {
  it('describes an untracked, a tracked and a retired tracked instrument', () => {
    expect(trackingStatusText(untrackedInfo)).toBe('Not tracked');
    expect(trackingStatusText({ ...untrackedInfo, deactivatedAt: '2026-10-12T05:00:00Z' })).toMatch(
      /^Not tracked \(untracked .*history kept\)$/,
    );
    expect(trackingStatusText(trackedInfo())).toMatch(/^Tracked since /);

    const waiting = trackingFacts(trackedInfo());
    expect(waiting.find((f) => f.label === 'Data coverage')?.value).toBe('History not collected yet');
    expect(waiting.find((f) => f.label === 'History')?.value).toMatch(/P1-12/);
    expect(trackingFacts(untrackedInfo).some((f) => f.label === 'History')).toBe(false);

    const retired = trackingFacts(trackedInfo({ retiredButTracked: true }));
    expect(retired.find((f) => f.label === 'Warning')?.value).toMatch(/No longer in Dhan's list/);

    const collected = trackingFacts(
      trackedInfo({ dataFrom: '2021-01-01', dataTo: '2026-10-09', lastUpdate: '2026-10-09T10:00:00Z' }),
    );
    expect(collected.find((f) => f.label === 'Data coverage')?.value).toBe('1 Jan 2021 to 9 Oct 2026');
    expect(collected.some((f) => f.label === 'History')).toBe(false);
  });
});

describe('tabs in the URL', () => {
  it('reads and writes the tab and the not-tracked switch', () => {
    const filters = parseFilters(new URLSearchParams('tab=tracked&untracked=1'));
    expect(filters.tab).toBe('tracked');
    expect(filters.untracked).toBe(true);
    expect(toParams(filters).toString()).toBe('tab=tracked&untracked=1');
    expect(toParams({ ...filters, tab: 'master', untracked: false }).toString()).toBe('');
    expect(parseFilters(new URLSearchParams('tab=bogus')).tab).toBe('master');
  });

  it('counts the master-only switches on the master list but not on the Tracked tab, and never the tab itself', () => {
    expect(activeFilterCount({ ...emptyFilters, tab: 'tracked' })).toBe(0);
    expect(activeFilterCount({ ...emptyFilters, includeRetired: true, untracked: true })).toBe(2);
    expect(activeFilterCount({ ...emptyFilters, tab: 'tracked', includeRetired: true, untracked: true })).toBe(0);
  });

  it('scopes a selection to the tab and the filters, not to the sort', () => {
    const base = selectionScope(emptyFilters);
    expect(selectionScope({ ...emptyFilters, sort: { field: 'lot', descending: true } })).toBe(base);
    expect(selectionScope({ ...emptyFilters, q: ' tcs ' })).toBe(selectionScope({ ...emptyFilters, q: 'tcs' }));
    expect(selectionScope({ ...emptyFilters, q: 'tcs' })).not.toBe(base);
    expect(selectionScope({ ...emptyFilters, tab: 'tracked' })).not.toBe(base);
    expect(selectionScope({ ...emptyFilters, untracked: true })).not.toBe(base);
  });
});
