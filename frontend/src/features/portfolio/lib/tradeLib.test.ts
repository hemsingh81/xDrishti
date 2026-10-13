import type { InfiniteData } from '@tanstack/react-query';
import { ApiError, type TradePage } from '@/shared/api';
import { buildRebuild, buildReport, buildSecurity, buildTrade, buildTradePage, tradeIds } from '@/test/tradeFixtures';
import { findTrade, withTrade, withVoidedTrade } from './tradeCache';
import { initialTradesAccount } from './accounts';
import { isOpeningLot, lotsFromTrades } from './tradeLots';
import { buildLot } from '@/test/fixtures';
import { capitalise } from '@/shared/lib/format';
import { staleReport, uploadErrorText } from './tradeErrors';
import { formatBytes, maxTradeFileBytes, validateTradeFile } from './tradeFile';
import {
  emptyTradeForm,
  parseAmount,
  placeServerErrors,
  toManualInput,
  validateTrade,
  type TradeForm,
} from './tradeForm';
import {
  costDeltaText,
  costWhy,
  countOf,
  importedText,
  notRebuiltReason,
  reasonText,
  rebuildTotals,
  replacedSentence,
  skippedParts,
} from './tradeText';
import { buildImportResult } from '@/test/tradeFixtures';

describe('validateTradeFile', () => {
  it.each([
    ['tradebook.csv', 10, undefined],
    ['TRADEBOOK.CSV', 10, undefined],
    ['notes.txt', 10, undefined],
    ['book.xlsx', 10, /not a CSV file/],
    ['book', 10, /not a CSV file/],
    ['empty.csv', 0, /is empty/],
    ['huge.csv', maxTradeFileBytes + 1, /the limit is 10 MB/],
    ['limit.csv', maxTradeFileBytes, undefined],
  ])('%s of %s bytes', (name, size, expected) => {
    const result = validateTradeFile({ name, size });
    if (expected) expect(result).toMatch(expected);
    else expect(result).toBeUndefined();
  });

  it('writes sizes in words', () => {
    expect(formatBytes(900)).toBe('900 B');
    expect(formatBytes(2048)).toBe('2 KB');
    expect(formatBytes(3.4 * 1024 * 1024)).toBe('3.4 MB');
    expect(formatBytes(10 * 1024 * 1024)).toBe('10 MB');
  });
});

describe('the Add trade form', () => {
  const valid: TradeForm = {
    ...emptyTradeForm,
    holdingId: 'h1',
    side: 'buy',
    tradeDate: '2026-10-05',
    quantity: '1,000',
    price: '1,250.50',
  };

  it('accepts a complete form', () => {
    expect(validateTrade(valid, false, '2026-10-13')).toEqual({});
  });

  it('does not need the security when the holding is given', () => {
    expect(validateTrade({ ...valid, holdingId: '' }, true, '2026-10-13')).toEqual({});
    expect(validateTrade({ ...valid, holdingId: '' }, false, '2026-10-13').security).toBe('Choose the security.');
  });

  it.each([
    [{ tradeDate: '2026-10-14' }, 'tradeDate', 'The date cannot be in the future.'],
    [{ tradeDate: '2026-10-13' }, undefined, undefined],
    [{ tradeDate: '1990-01-01' }, 'tradeDate', 'The date cannot be before 2 Jan 1990.'],
    [{ tradeDate: '1990-01-02' }, undefined, undefined],
    [{ tradeDate: '2026-02-30' }, 'tradeDate', 'Enter a valid date.'],
    [{ quantity: '0.5' }, 'quantity', 'The quantity must be a whole number of shares.'],
    [{ quantity: '-1' }, 'quantity', 'The quantity must be above 0.'],
    [{ quantity: 'abc' }, 'quantity', 'Enter the quantity as a plain number, for example 10.'],
    [{ quantity: '' }, 'quantity', 'Enter the quantity.'],
    [{ price: '1e3' }, 'price', 'Enter the price as a plain number, for example 1250.50.'],
    [{ price: '0' }, 'price', 'The price must be above 0.'],
    [{ charges: 'x' }, 'charges', 'Enter the charges as a number, or leave them empty.'],
    [{ charges: '0' }, undefined, undefined],
    [{ side: '' as const }, 'side', 'Choose buy or sell.'],
  ])('checks %j', (patch, field, message) => {
    const errors = validateTrade({ ...valid, ...patch }, false, '2026-10-13');
    if (field) expect(errors[field as keyof typeof errors]).toBe(message);
    else expect(errors).toEqual({});
  });

  it('needs a symbol or an ISIN for a typed security', () => {
    const typed = { ...valid, holdingId: '', typed: true };
    expect(validateTrade(typed, false, '2026-10-13').security).toMatch(/symbol or the ISIN/);
    expect(validateTrade({ ...typed, isin: 'INE009A01021' }, false, '2026-10-13')).toEqual({});
  });

  it('reads numbers with thousands commas', () => {
    expect(parseAmount('1,25,000.50')).toBe(125_000.5);
    expect(parseAmount('')).toBeNaN();
  });

  it.each([
    'Infinity',
    '-Infinity',
    '1e400',
    '1e3',
    '0x10',
    '1.',
    '.5',
    '1..2',
    '12345678901234',
    '1.1234567',
    '1 000',
    '--1',
    'NaN',
  ])('does not read %s as a number', (text) => {
    expect(parseAmount(text)).toBeNaN();
  });

  it.each([
    ['10', 10],
    ['-3', -3],
    ['0.0001', 0.0001],
    ['123456789012', 123_456_789_012],
    [' 1,250.50 ', 1250.5],
  ])('reads %s', (text, value) => {
    expect(parseAmount(text)).toBe(value);
  });

  it('builds the request for a holding, for a typed security and for the given holding', () => {
    expect(toManualInput({ ...valid, tradeTime: '09:15', charges: '12' }, 'a1', null)).toEqual({
      accountId: 'a1',
      holdingId: 'h1',
      symbol: null,
      isin: null,
      side: 'buy',
      tradeDate: '2026-10-05',
      tradeTime: '09:15:00',
      quantity: 1000,
      price: 1250.5,
      charges: 12,
    });
    expect(toManualInput({ ...valid, holdingId: '', typed: true, symbol: ' wipro ' }, 'a1', null)).toMatchObject({
      holdingId: null,
      symbol: 'wipro',
      isin: null,
      charges: null,
    });
    expect(toManualInput({ ...valid, holdingId: '' }, 'a1', 'given')).toMatchObject({ holdingId: 'given' });
  });

  it('places the API’s messages at the form’s fields', () => {
    const { errors, unplaced } = placeServerErrors({
      holdingId: 'The holding does not exist.',
      Symbol: 'Not an NSE share.',
      quantity: 'Whole shares.',
      weird: 'Something.',
    });
    expect(errors).toEqual({ security: 'The holding does not exist. Not an NSE share.', quantity: 'Whole shares.' });
    expect(unplaced).toEqual(['Something.']);
  });
});

describe('trade text', () => {
  it('counts', () => {
    expect(countOf(1, 'trade')).toBe('1 trade');
    expect(countOf(1_200, 'trade')).toBe('1,200 trades');
    expect(countOf(2, 'security', 'securities')).toBe('2 securities');
  });

  it('says which lots a rebuild replaces', () => {
    expect(replacedSentence(0, 0)).toBeNull();
    expect(replacedSentence(1, 0)).toBe('1 hand-dated lot will be replaced');
    expect(replacedSentence(3, 2)).toBe('3 hand-dated lots and 2 lots from syncs will be replaced');
    expect(replacedSentence(0, 1)).toBe('1 lot from syncs will be replaced');
  });

  it('adds up only the securities that would be rebuilt', () => {
    const totals = rebuildTotals([
      buildSecurity({
        rebuild: buildRebuild({ lotsBefore: 2, lotsAfter: 4, handDatedLotsReplaced: 1, adjustedFor: 1 }),
      }),
      buildSecurity({ rebuild: buildRebuild({ status: 'unchanged', lotsBefore: 9 }) }),
      buildSecurity({ rebuild: buildRebuild({ lotsBefore: 1, lotsAfter: 1, syncLotsReplaced: 1 }) }),
    ]);
    expect(totals).toEqual({ holdings: 2, lotsBefore: 3, lotsAfter: 5, handDated: 1, sync: 1, adjusted: 1 });
  });

  it('treats a status it does not know as a mismatch, with numbers when the API gives them', () => {
    expect(reasonText(buildRebuild({ status: 'strange', reason: null, q: 5, net: 9 }))).toBe(
      'The trades do not match what Dhan says you hold. Dhan shows 5 shares; the trades add up to a net 9. Nothing is applied.',
    );
    expect(reasonText(buildRebuild({ status: 'ok' }))).toBeNull();
  });

  it('shows a cost difference only from a rupee up', () => {
    expect(costDeltaText(buildRebuild({ costDelta: 0.99 }))).toBeNull();
    expect(costDeltaText(buildRebuild({ costDelta: null }))).toBeNull();
    expect(costDeltaText(buildRebuild({ costDelta: 1_500 }))).toBe(
      'Cost basis from your trades is ₹1,500 higher than the broker’s cost',
    );
    expect(costDeltaText(buildRebuild({ costDelta: -1_234 }))).toBe(
      'Cost basis from your trades is ₹1,234 lower than the broker’s cost',
    );
  });

  it('explains a cost difference by case: opening lot with sells, no opening lot, and neither', () => {
    const withOpening = { openingQty: { before: 5, after: 5 } };
    const without = { openingQty: { before: 0, after: 0 } };
    expect(costWhy(buildRebuild({ ...withOpening, realisedPnl: 100 }))).toMatch(
      /first-in first-out.*after sales the two differ/,
    );
    expect(costWhy(buildRebuild({ ...withOpening, realisedPnl: 0 }), 2)).toMatch(/first-in first-out/);
    expect(costWhy(buildRebuild({ ...without, realisedPnl: 0 }), 0)).toMatch(
      /explain the whole holding.*real buy prices in the file.*average cost\. The broker/,
    );
    expect(costWhy(buildRebuild({ ...without, realisedPnl: 50 }))).toMatch(
      /real buy prices.*sales are costed first-in first-out/,
    );
    expect(costWhy(buildRebuild({ ...withOpening, realisedPnl: 0 }), 0)).toMatch(/differs from your broker’s figure/);
  });

  it('names why an undo could not rebuild a holding', () => {
    expect(notRebuiltReason('ambiguous')).toBe('more than one holding matches');
    expect(notRebuiltReason('negative_opening')).toMatch(/explain more shares/);
    expect(notRebuiltReason('sell_exceeds_open')).toMatch(/sell is larger/);
    expect(notRebuiltReason('fractional_after_adjustment')).toMatch(/fraction of a share/);
    expect(notRebuiltReason('x')).toMatch(/do not match/);
  });

  it('orders the skipped reasons by count and names unknown ones as they are', () => {
    expect(
      skippedParts({ bse: 2, derivatives: 9, commodity: 0, odd: 2 }).map((p) => `${p.label}:${String(p.count)}`),
    ).toEqual(['Derivatives:9', 'BSE only:2', 'odd:2']);
  });

  it('writes the import outcome', () => {
    expect(importedText(buildImportResult())).toBe('Imported 10 trades. Rebuilt 1 holding, 1 lot replaced.');
    expect(importedText(buildImportResult({ rebuilt: false, alreadyImported: 2 }))).toBe(
      'Imported 10 trades (2 were stored already). Your lots were not changed.',
    );
    expect(importedText(buildImportResult({ batch: null, imported: 0, alreadyImported: 1 }))).toBe(
      'Nothing new to import: the 1 trade in the file were stored already.',
    );
  });
});

describe('trade errors', () => {
  const problem = (status: number, body: object) => new ApiError(status, body);

  it('takes the fresh preview from a stale-preview conflict, and nothing from other errors', () => {
    const report = buildReport({ digest: 'fresh' });
    expect(staleReport(problem(409, { code: 'portfolio.preview_stale', report }))?.digest).toBe('fresh');
    expect(staleReport(problem(409, { code: 'portfolio.preview_stale', report: { nonsense: true } }))).toBeNull();
    expect(staleReport(problem(409, { code: 'portfolio.sync_running' }))).toBeNull();
    expect(staleReport(new Error('x'))).toBeNull();
  });

  it('writes the sentence for an upload that failed', () => {
    expect(uploadErrorText(problem(415, {}), 'x')).toMatch(/did not accept the upload/);
    expect(
      uploadErrorText(
        problem(400, { code: 'portfolio.invalid', errors: { file: ['Bad file.'], accountId: ['No account.'] } }),
        'x',
      ),
    ).toBe('Bad file. No account.');
    expect(uploadErrorText(problem(500, { title: 'Boom' }), 'x')).toBe('Boom');
  });
});

describe('withVoidedTrade', () => {
  it('strikes out the one trade in whichever page holds it', () => {
    const data: InfiniteData<TradePage> = {
      pageParams: [1, 2],
      pages: [buildTradePage([buildTrade()]), buildTradePage([buildTrade({ id: tradeIds.second })], { page: 2 })],
    };

    const next = withVoidedTrade(data, tradeIds.second, '2026-10-13T08:00:00Z');

    expect(next.pages[0]?.items[0]?.voided).toBe(false);
    expect(next.pages[1]?.items[0]).toMatchObject({ voided: true, voidedAt: '2026-10-13T08:00:00Z' });
    expect(data.pages[1]?.items[0]?.voided).toBe(false);
  });
});

describe('rolling back one trade', () => {
  const data = (): InfiniteData<TradePage> => ({
    pageParams: [1],
    pages: [buildTradePage([buildTrade(), buildTrade({ id: tradeIds.second })])],
  });

  it('finds a trade in the cached lists', () => {
    expect(findTrade([undefined, data()], tradeIds.second)?.id).toBe(tradeIds.second);
    expect(findTrade([data()], 'nope')).toBeUndefined();
  });

  it('puts back only that trade and leaves the others as they are now', () => {
    const original = buildTrade();
    const both = withVoidedTrade(withVoidedTrade(data(), tradeIds.first, 'now'), tradeIds.second, 'now');

    const next = withTrade(both, original);

    expect(next.pages[0]?.items.map((trade) => trade.voided)).toEqual([false, true]);
  });
});

describe('the opening lot of a rebuilt holding', () => {
  const imported = buildLot({ id: 'a', source: 'import', usesDefaultDate: true });
  const fromTrade = buildLot({ id: 'b', source: 'trade' });
  const realClosed = buildLot({
    id: 'c',
    source: 'import',
    status: 'closed',
    estimated: false,
    closedOn: '2026-09-01',
  });
  const estimatedClosed = buildLot({
    id: 'd',
    source: 'import',
    status: 'closed',
    estimated: true,
    closedOn: '2026-09-01',
  });

  it('is the open imported lot of a holding with trade lots', () => {
    expect(isOpeningLot([imported, fromTrade], imported)).toBe(true);
    expect(isOpeningLot([imported, fromTrade], fromTrade)).toBe(false);
  });

  it('also holds for a holding rebuilt from sells only (its closed pieces have real prices)', () => {
    expect(lotsFromTrades([imported, realClosed])).toBe(true);
    expect(isOpeningLot([imported, realClosed], imported)).toBe(true);
    expect(isOpeningLot([imported, realClosed], realClosed)).toBe(false);
  });

  it('is nothing for a holding that was not built from trades, estimated closures included', () => {
    expect(lotsFromTrades([imported, estimatedClosed])).toBe(false);
    expect(isOpeningLot([imported, estimatedClosed], imported)).toBe(false);
    expect(isOpeningLot([imported, buildLot({ id: 'e', source: 'added' })], imported)).toBe(false);
  });
});

describe('small helpers', () => {
  it('capitalises the first letter only', () => {
    expect(capitalise('3 hand-dated lots')).toBe('3 hand-dated lots');
    expect(capitalise('hand-dated lots will be replaced')).toBe('Hand-dated lots will be replaced');
    expect(capitalise('')).toBe('');
  });

  it('starts the trades on the page’s account, or the only one, or none', () => {
    const two = [{ id: 'a' }, { id: 'b' }];
    expect(initialTradesAccount(two, 'b')).toBe('b');
    expect(initialTradesAccount(two, null)).toBe('');
    expect(initialTradesAccount(two, 'zzz')).toBe('');
    expect(initialTradesAccount([{ id: 'a' }], null)).toBe('a');
    expect(initialTradesAccount([], null)).toBe('');
  });
});
