import { buildAccountDetail } from '@/test/fixtures';
import { autoRefreshFrom, toRefreshInput, validateAutoRefresh } from './autoRefresh';
import { portfolioFormFrom, toPortfolioInput, validatePortfolio } from './portfolioForm';

const offRefresh = { refreshMode: 'off', refreshDailyTimeIst: '16:00', firstImportDate: '2025-01-01' };

describe('portfolio form', () => {
  it('reads the settings and sends them without any bucket (the API then keeps the account’s own)', () => {
    const state = portfolioFormFrom(buildAccountDetail().portfolio);

    expect(state).toEqual({
      includeInTotals: true,
      refresh: { mode: 'off', dailyTime: '16:00' },
      firstImportDate: '2025-01-01',
    });
    const body = toPortfolioInput(state);
    expect(body).toEqual({ includeInTotals: true, ...offRefresh });
    expect(body).not.toHaveProperty('defaultBucket');
    expect(JSON.stringify(body)).not.toMatch(/bucket/i);
    expect(body).not.toHaveProperty('historyFrom');
  });

  it('defaults the first import date to 2025-01-01 when nothing is stored', () => {
    expect(portfolioFormFrom().firstImportDate).toBe('2025-01-01');
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
