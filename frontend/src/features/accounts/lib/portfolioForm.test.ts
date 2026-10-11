import { buildAccountDetail, buildBucket, buildBuckets, ids } from '@/test/fixtures';
import { autoRefreshFrom, toRefreshInput, validateAutoRefresh } from './autoRefresh';
import {
  autoBucketNameOf,
  customBuckets,
  portfolioFormFrom,
  toPortfolioInput,
  validatePortfolio,
} from './portfolioForm';

const offRefresh = { refreshMode: 'off', refreshDailyTimeIst: '16:00', firstImportDate: '2025-01-01' };

describe('portfolio form', () => {
  it('names the account’s own bucket after the account', () => {
    expect(autoBucketNameOf('  Dhan · Main ')).toBe('Dhan · Main · Portfolio');
  });

  it('shows a placeholder until the account has a name', () => {
    expect(autoBucketNameOf('  ')).toBe('Account name · Portfolio');
  });

  it('keeps the own bucket as blank and sends it as an empty string (null would keep the current choice)', () => {
    const state = portfolioFormFrom(buildAccountDetail().portfolio);

    expect(state).toEqual({
      defaultBucket: '',
      includeInTotals: true,
      refresh: { mode: 'off', dailyTime: '16:00' },
      firstImportDate: '2025-01-01',
    });
    expect(toPortfolioInput(state)).toEqual({ defaultBucket: '', includeInTotals: true, ...offRefresh });
    expect(toPortfolioInput(state)).not.toHaveProperty('historyFrom');
  });

  it('keeps a custom bucket by name', () => {
    const portfolio = { ...buildAccountDetail().portfolio, defaultBucket: 'MyLongTerm' };

    expect(toPortfolioInput(portfolioFormFrom(portfolio))).toEqual({
      defaultBucket: 'MyLongTerm',
      includeInTotals: true,
      ...offRefresh,
    });
  });

  it('reads the saved refresh settings and sends them back', () => {
    const portfolio = {
      ...buildAccountDetail().portfolio,
      refreshMode: 'daily',
      refreshDailyTimeIst: '09:45',
    };

    const state = portfolioFormFrom(portfolio);

    expect(state.refresh).toEqual({ mode: 'daily', dailyTime: '09:45' });
    expect(toPortfolioInput(state)).toMatchObject({
      refreshMode: 'daily',
      refreshDailyTimeIst: '09:45',
    });
  });

  it('offers custom buckets only, never any account’s default bucket', () => {
    const other = buildBucket({ name: 'Other · Portfolio', isAccountDefault: true, accountId: ids.swing });
    const custom = buildBucket({ name: 'MyLongTerm' });

    expect(customBuckets([...buildBuckets(), other, custom])).toEqual([custom]);
  });
});

describe('auto refresh form', () => {
  it('treats an unknown mode from the API as off', () => {
    const settings = { refreshMode: 'interval', refreshDailyTimeIst: '16:00' };

    expect(autoRefreshFrom(settings).mode).toBe('off');
  });

  it('checks the daily time only while the mode is daily', () => {
    const base = { mode: 'off', dailyTime: '' } as const;

    expect(validateAutoRefresh(base)).toEqual({});
    expect(validateAutoRefresh({ ...base, mode: 'daily' })).toHaveProperty('refreshDailyTimeIst');
    expect(validateAutoRefresh({ ...base, mode: 'daily', dailyTime: '24:00' })).toHaveProperty('refreshDailyTimeIst');
    expect(validateAutoRefresh({ ...base, mode: 'daily', dailyTime: '09:05' })).toEqual({});
  });

  it('leaves out an unusable value the current mode ignores, so the server keeps its own', () => {
    expect(toRefreshInput({ mode: 'daily', dailyTime: '' })).toEqual({
      refreshMode: 'daily',
      refreshDailyTimeIst: undefined,
    });
  });
});

describe('portfolio validation', () => {
  it('checks the first import date as well as the refresh time', () => {
    const state = portfolioFormFrom(buildAccountDetail().portfolio);

    expect(validatePortfolio(state)).toEqual({});
    expect(validatePortfolio({ ...state, firstImportDate: '' })).toHaveProperty('firstImportDate');
    expect(validatePortfolio({ ...state, firstImportDate: '2999-01-01' })).toHaveProperty('firstImportDate');
    expect(validatePortfolio({ ...state, firstImportDate: '1989-12-31' })).toHaveProperty('firstImportDate');
  });
});
