import { buildPaletteItems, filterPaletteItems } from './paletteItems';

describe('palette items: My account', () => {
  it('offers an action that opens /account and is found by "account", "profile" and "password"', () => {
    const go = vi.fn();
    const items = buildPaletteItems({ accounts: [], go, toggleMode: vi.fn() });

    const action = items.find((item) => item.label === 'My account');
    expect(action).toMatchObject({ group: 'Actions', hint: '/account' });
    action?.run();
    expect(go).toHaveBeenCalledWith('/account');

    for (const query of ['my account', 'profile', 'password']) {
      expect(filterPaletteItems(items, query).map((item) => item.label)).toContain('My account');
    }
  });
});
