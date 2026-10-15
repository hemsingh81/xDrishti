import {
  buildBrokerCounts,
  buildBrokerKind,
  buildBrokerRun,
  buildDroppedRow,
  buildDroppingKind,
} from '@/test/brokerHistoryFixtures';
import { dropReasonText, locationText, summariseDrops } from './dropReasons';
import { summariseKind } from './brokerText';

describe('dropReasonText', () => {
  it.each([
    ['unreadable_row', 'unreadable row'],
    ['missing_id', 'no trade id'],
    ['bad_id', 'trade id too long'],
    ['missing_side', 'side is not buy or sell'],
    ['bad_quantity', 'quantity missing or not a whole positive number'],
    ['bad_price', 'price unreadable'],
    ['bad_time', 'time unreadable'],
    ['bad_date', 'date unreadable'],
    ['missing_amount', 'no amount'],
    ['bad_amount', 'amount unreadable'],
    ['zero_amount', 'zero amount (no money moved, e.g. an opening balance)'],
    ['failed_checks', 'broke a storage rule'],
    ['store_refused', 'the database refused it'],
    ['other', 'other'],
  ])('words %s as "%s"', (code, text) => {
    expect(dropReasonText(code)).toBe(text);
  });

  it('reads "other" for a code it does not know, never the code itself', () => {
    expect(dropReasonText('<b>surprise</b>')).toBe('other');
  });
});

describe('summariseDrops', () => {
  it('lists the reasons largest first and says how many rows in all', () => {
    const kind = buildDroppingKind({ bad_time: 2, bad_quantity: 9, missing_id: 4 });
    expect(summariseDrops('trades', kind)?.text).toBe(
      '15 rows could not be read: 9 quantity missing or not a whole positive number, 4 no trade id, 2 time unreadable',
    );
  });

  it('breaks a tie in the order of the reason list', () => {
    const kind = buildDroppingKind({ bad_price: 3, missing_id: 3 });
    expect(summariseDrops('trades', kind)?.text).toBe('6 rows could not be read: 3 no trade id, 3 price unreadable');
  });

  it('names three reasons at most and counts the others', () => {
    const kind = buildDroppingKind({ bad_quantity: 9, missing_id: 4, bad_time: 2, bad_price: 1, other: 1 });
    expect(summariseDrops('trades', kind)?.text).toBe(
      '17 rows could not be read: 9 quantity missing or not a whole positive number, 4 no trade id, 2 time unreadable, +2 more',
    );
  });

  it('says "1 row" for one', () => {
    expect(summariseDrops('ledger', buildDroppingKind({ bad_date: 1 }))?.text).toBe(
      '1 row could not be read: 1 date unreadable',
    );
  });

  it('is quiet when only lines where no money moved were dropped', () => {
    expect(summariseDrops('ledger', buildDroppingKind({ zero_amount: 2 }))).toMatchObject({
      quiet: true,
      text: 'lines where no money moved ignored',
      locations: [],
    });
  });

  it('names the no-money lines when other reasons come with them', () => {
    const drops = summariseDrops('ledger', buildDroppingKind({ zero_amount: 2, bad_date: 1 }));
    expect(drops).toMatchObject({ quiet: false });
    expect(drops?.text).toBe(
      '3 rows could not be read: 2 zero amount (no money moved, e.g. an opening balance), 1 date unreadable',
    );
  });

  it('has nothing to say when no row was dropped or the API does not say (an older run)', () => {
    expect(summariseDrops('trades', buildBrokerKind())).toBeNull();
    expect(summariseDrops('trades', buildDroppingKind({}))).toBeNull();
    expect(summariseDrops('trades', buildDroppingKind({ bad_price: 0 }))).toBeNull();
    // Dropped rows were counted but no reason was recorded: nothing to explain.
    const old = buildBrokerKind({ lastRun: buildBrokerRun({ counts: buildBrokerCounts({ dropped: 3 }) }) });
    expect(summariseDrops('trades', old)).toBeNull();
  });

  it('falls back to the run counts when the kind carries no reasons', () => {
    const kind = buildBrokerKind({
      lastRun: buildBrokerRun({ counts: buildBrokerCounts({ dropped: 2, droppedByReason: { bad_price: 2 } }) }),
    });
    expect(summariseDrops('trades', kind)?.text).toBe('2 rows could not be read: 2 price unreadable');
  });
});

describe('locationText', () => {
  it('counts the page and the row from 1 and writes the window as dates', () => {
    expect(locationText(buildDroppedRow(), true)).toBe('window 1 Aug–31 Aug, page 1, row 3: price unreadable');
  });

  it('leaves the page out for the ledger, and writes one day once', () => {
    expect(locationText(buildDroppedRow({ windowTo: '2026-08-01', reason: 'bad_date' }), false)).toBe(
      'window 1 Aug, row 3: date unreadable',
    );
  });

  it('adds the year to a window that crosses years', () => {
    expect(locationText(buildDroppedRow({ windowFrom: '2025-12-20', windowTo: '2026-01-19' }), true)).toBe(
      'window 20 Dec 2025–19 Jan 2026, page 1, row 3: price unreadable',
    );
  });
});

describe('summariseKind drops', () => {
  it('carries them for a finished run and not while it runs or when it was skipped', () => {
    expect(summariseKind('trades', buildDroppingKind({ bad_price: 1 })).drops).not.toBeNull();
    const running = buildDroppingKind({ bad_price: 1 }, [], {
      lastRun: buildBrokerRun({ status: 'running', finishedAt: null }),
    });
    expect(summariseKind('trades', running).drops).toBeNull();
    const skipped = buildDroppingKind({ bad_price: 1 }, [], { lastRun: buildBrokerRun({ status: 'skipped' }) });
    expect(summariseKind('trades', skipped).drops).toBeNull();
    expect(summariseKind('trades', buildBrokerKind()).drops).toBeNull();
  });
});
