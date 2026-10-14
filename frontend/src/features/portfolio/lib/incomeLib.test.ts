import { buildIncome } from '@/test/dividendFixtures';
import { emptyDividendForm, placeDividendErrors, toDividendInput, validateDividend } from './dividendForm';
import { incomeAt, round4 } from './liveFigures';
import {
  costsHint,
  coverageHint,
  dividendsHint,
  fromText,
  ledgerChargesNote,
  netXirrOf,
  unallocatedOf,
} from './income';
import { groupSortOptions } from './groups';

describe('incomeAt: the total return figures at a live profit', () => {
  const income = buildIncome({ dividends: 1_200, charges: 150, totalReturnPct: 1, netTotalReturnPct: 1 });

  it('works out (realised + profit + dividends) over invested, and the same less the charges, rounded once', () => {
    const live = incomeAt(income, 500, 100_000, 16_100);
    expect(live?.totalReturnPct).toBe(round4(((500 + 16_100 + 1_200) / 100_000) * 100));
    expect(live?.totalReturnPct).toBe(17.8);
    expect(live?.netTotalReturnPct).toBe(17.65);
  });

  it('leaves everything that does not move with a price as it was read', () => {
    const live = incomeAt(income, 0, 100_000, 16_100);
    expect(live).toMatchObject({
      dividends: 1_200,
      charges: 150,
      unallocated: income.unallocated,
      netXirr: income.netXirr,
      netXirrState: income.netXirrState,
      coverageFrom: income.coverageFrom,
      dividendsFrom: income.dividendsFrom,
      chargesFrom: income.chargesFrom,
      tradeChargesFrom: income.tradeChargesFrom,
      ledgerChargesFrom: income.ledgerChargesFrom,
      ledgerCharges: income.ledgerCharges,
    });
  });

  it('is nothing without invested capital or without a usable price, like the server', () => {
    expect(incomeAt(income, 0, 0, 100)).toMatchObject({ totalReturnPct: null, netTotalReturnPct: null });
    expect(incomeAt(income, 0, 100_000, null)).toMatchObject({ totalReturnPct: null, netTotalReturnPct: null });
  });

  it('keeps a loss negative and rounds halves away from zero at four decimals', () => {
    const live = incomeAt(buildIncome({ dividends: 0, charges: 0 }), 0, 30_000, -1);
    expect(live?.totalReturnPct).toBe(-0.0033);
    expect(incomeAt(buildIncome({ dividends: 0, charges: 0 }), 0, 80_000, 1)?.totalReturnPct).toBe(0.0013);
  });

  it('is no income when none is recorded: null and undefined stay as they are', () => {
    expect(incomeAt(null, 0, 100, 1)).toBeNull();
    expect(incomeAt(undefined, 0, 100, 1)).toBeUndefined();
  });
});

describe('the words of the income figures', () => {
  it('says since when, or that nothing is there yet', () => {
    expect(fromText('2025-12-05', 'none')).toBe('from 5 Dec 2025');
    expect(fromText(null, 'none entered yet')).toBe('none entered yet');
    const income = buildIncome({
      dividendsFrom: null,
      chargesFrom: null,
      tradeChargesFrom: null,
      ledgerChargesFrom: null,
      coverageFrom: null,
    });
    expect(dividendsHint(income)).toBe('none entered yet');
    expect(costsHint(income)).toBe('none read yet');
    expect(coverageHint(income)).toBe('nothing recorded yet');
  });

  it('shows each source on its own day, and only the sources that exist', () => {
    expect(dividendsHint(buildIncome({ dividendsFrom: '2025-12-05' }))).toBe('entered from 5 Dec 2025');
    expect(costsHint(buildIncome({ tradeChargesFrom: '2025-01-02', ledgerChargesFrom: null }))).toBe(
      'trade charges from 2 Jan 2025',
    );
    expect(costsHint(buildIncome({ tradeChargesFrom: null, ledgerChargesFrom: '2026-01-05' }))).toBe(
      'account costs from 5 Jan 2026',
    );
    expect(costsHint(buildIncome({ tradeChargesFrom: '2025-01-02', ledgerChargesFrom: '2026-01-05' }))).toBe(
      'trade charges from 2 Jan 2025 · account costs from 5 Jan 2026',
    );
    expect(coverageHint(buildIncome({ coverageFrom: '2025-12-05' }))).toBe('counted from 5 Dec 2025');
  });

  it('mentions the account costs only when there are some', () => {
    const format = (value: number) => `₹${value}`;
    expect(ledgerChargesNote(buildIncome({ ledgerCharges: 120 }), format)).toBe('of which account costs ₹120');
    expect(ledgerChargesNote(buildIncome({ ledgerCharges: 0 }), format)).toBeNull();
    expect(ledgerChargesNote(buildIncome({ ledgerCharges: null }), format)).toBeNull();
  });

  it('gives the net XIRR as a number, or the reason there is none as for the gross XIRR', () => {
    expect(netXirrOf(buildIncome({ netXirr: 13.8 }))).toEqual({ value: 13.8 });
    expect(netXirrOf(buildIncome({ netXirr: null, netXirrState: 'insufficient' }))).toMatchObject({
      label: '—',
      short: 'Needs 30+ days of history',
    });
    expect(netXirrOf(buildIncome({ netXirr: null, netXirrState: 'n_m' }))).toMatchObject({ label: 'n/m' });
  });

  it('mentions the unallocated dividends only when there are some', () => {
    expect(unallocatedOf(buildIncome({ unallocated: 200 }))).toBe(200);
    expect(unallocatedOf(buildIncome({ unallocated: 0 }))).toBeNull();
    expect(unallocatedOf(buildIncome({ unallocated: null }))).toBeNull();
  });

  it('offers the order by dividends in both directions on a phone', () => {
    expect(groupSortOptions.filter((option) => option.field === 'dividends').map((o) => o.label)).toEqual([
      'Dividends, most first',
      'Dividends, least first',
    ]);
  });
});

describe('the Add dividend form', () => {
  const valid = { ...emptyDividendForm, payDate: '2026-10-01', amount: '1,250.50' };

  it('accepts a date, an amount and nothing else', () => {
    expect(validateDividend(valid, 'acc', '2026-10-14')).toEqual({});
  });

  it('needs the account, the date and the amount', () => {
    expect(validateDividend(emptyDividendForm, '', '2026-10-14')).toEqual({
      account: 'Choose the account.',
      payDate: 'Enter the day the money was credited.',
      amount: 'Enter the amount you received.',
    });
  });

  it.each([
    ['a date in the future', { payDate: '2026-10-15' }, 'payDate', 'The date cannot be in the future.'],
    ['a day that does not exist', { payDate: '2026-02-30' }, 'payDate', 'Enter a valid date.'],
    ['a date before 1990', { payDate: '1989-12-31' }, 'payDate', 'The date cannot be before 2 Jan 1990.'],
    ['an amount of zero', { amount: '0' }, 'amount', 'The amount must be above 0.'],
    ['a fraction of a paisa', { amount: '10.005' }, 'amount', 'Enter rupees and paise: at most two decimals.'],
    ['an amount in words', { amount: 'ten' }, 'amount', 'Enter the amount as a plain number, for example 1250.50.'],
    ['a per-share amount of zero', { perShare: '0' }, 'perShare', 'The amount per share must be above 0.'],
    ['a note of 201 characters', { note: 'x'.repeat(201) }, 'note', 'The note can have at most 200 characters.'],
  ] as const)('refuses %s', (_name, patch, field, message) => {
    expect(validateDividend({ ...valid, ...patch }, 'acc', '2026-10-14')[field]).toBe(message);
  });

  it('asks for the holding, or, without one, for the account; confirm only when it is repeated', () => {
    const form = { ...valid, perShare: '31.25', note: ' Interim ' };
    expect(toDividendInput(form, 'acc', false)).toEqual({
      accountId: 'acc',
      holdingId: null,
      payDate: '2026-10-01',
      amount: 1250.5,
      perShare: 31.25,
      note: 'Interim',
      confirm: null,
    });
    expect(toDividendInput({ ...form, holdingId: 'h1' }, 'acc', true)).toMatchObject({
      accountId: null,
      holdingId: 'h1',
      confirm: true,
    });
  });

  it("places the API's messages at the form's fields and returns the rest apart", () => {
    expect(placeDividendErrors({ amount: 'Bad.', PayDate: 'Late.', holdingId: 'Gone.', mystery: 'Odd.' })).toEqual({
      errors: { amount: 'Bad.', payDate: 'Late.', account: 'Gone.' },
      unplaced: ['Odd.'],
    });
  });
});
