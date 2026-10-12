import { buildAccountDetail, buildSwingAccount, summaryOf } from '@/test/fixtures';
import { nextStepHint } from './connection';
import { accountsNeedingAction } from './needingAction';

const main = summaryOf(buildAccountDetail());
const swing = summaryOf(buildSwingAccount());

describe('accounts needing action', () => {
  it('lists expired and not-connected accounts and flags expiry as the urgent case', () => {
    const result = accountsNeedingAction([
      { account: main, status: 'expired' },
      { account: swing, status: 'not_connected' },
    ]);

    expect(result.count).toBe(2);
    expect(result.expired).toBe(true);
    expect(result.items).toEqual([
      { id: main.id, name: main.name, status: 'expired' },
      { id: swing.id, name: swing.name, status: 'not_connected' },
    ]);
  });

  it('is not urgent when accounts are only not connected, and empty when all are connected', () => {
    expect(accountsNeedingAction([{ account: swing, status: 'not_connected' }]).expired).toBe(false);
    expect(accountsNeedingAction([{ account: main, status: 'connected' }])).toEqual({
      count: 0,
      expired: false,
      items: [],
    });
  });
});

describe('next step hint', () => {
  it('says what to do, and nothing once connected', () => {
    expect(nextStepHint('expired', true)).toMatch(/expired — reconnect/);
    expect(nextStepHint('not_connected', true)).toMatch(/connect to import/);
    expect(nextStepHint('not_connected', false)).toMatch(/Add the API key & secret/);
    expect(nextStepHint('connected', true)).toBeNull();
  });
});
