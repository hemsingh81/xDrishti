import { act, renderHook } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MemoryRouter, useLocation } from 'react-router';
import { useUrlState } from './useUrlState';

const tabs = ['overview', 'setups'] as const;

function setup(initial: string) {
  const wrapper = ({ children }: { children: ReactNode }) => (
    <MemoryRouter initialEntries={[initial]}>{children}</MemoryRouter>
  );
  return renderHook(() => ({ url: useUrlState(), search: useLocation().search }), { wrapper });
}

describe('useUrlState', () => {
  it('reads a value of the allowed set and falls back to the default for anything else', () => {
    expect(setup('/x?tab=setups').result.current.url.choice('tab', tabs, 'overview')).toBe('setups');
    expect(setup('/x?tab=bogus').result.current.url.choice('tab', tabs, 'overview')).toBe('overview');
    expect(setup('/x').result.current.url.choice('tab', tabs, 'overview')).toBe('overview');
  });

  it('reads free text, and an empty or missing one as null', () => {
    expect(setup('/x?cell=a%7Cb').result.current.url.text('cell')).toBe('a|b');
    expect(setup('/x?cell=').result.current.url.text('cell')).toBeNull();
    expect(setup('/x').result.current.url.text('cell')).toBeNull();
  });

  it('sets a parameter, keeps the others, and removes one given null', () => {
    const view = setup('/x?window=60d&tab=setups');
    act(() => {
      view.result.current.url.update({ cell: 'a|b', tab: null });
    });
    const params = new URLSearchParams(view.result.current.search);
    expect(params.get('window')).toBe('60d');
    expect(params.get('cell')).toBe('a|b');
    expect(params.has('tab')).toBe(false);
  });
});
