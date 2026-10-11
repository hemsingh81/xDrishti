import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { SearchField } from './SearchField';

function Harness({ onCommit }: { onCommit: (value: string) => void }) {
  const [value, setValue] = useState('');
  return (
    <>
      <SearchField
        value={value}
        label="Search things"
        placeholder="Type…"
        onCommit={(next) => {
          setValue(next);
          onCommit(next);
        }}
      />
      <button
        type="button"
        onClick={() => {
          setValue('from outside');
        }}
      >
        Set from outside
      </button>
    </>
  );
}

describe('SearchField', () => {
  it('commits once, after the typing stops', async () => {
    const commits: string[] = [];
    render(<Harness onCommit={(v) => commits.push(v)} />);

    await userEvent.type(screen.getByRole('searchbox', { name: 'Search things' }), 'tcs');

    expect(commits).toEqual([]);
    await vi.waitFor(() => {
      expect(commits).toEqual(['tcs']);
    });
  });

  it('clears at once with the clear button', async () => {
    const commits: string[] = [];
    render(<Harness onCommit={(v) => commits.push(v)} />);
    await userEvent.type(screen.getByRole('searchbox', { name: 'Search things' }), 'a');
    await vi.waitFor(() => {
      expect(commits).toEqual(['a']);
    });

    await userEvent.click(screen.getByRole('button', { name: 'Clear search' }));

    expect(screen.getByRole('searchbox', { name: 'Search things' })).toHaveValue('');
    expect(commits.at(-1)).toBe('');
  });

  it('follows a value that changes from outside', async () => {
    render(<Harness onCommit={() => undefined} />);

    await userEvent.click(screen.getByRole('button', { name: 'Set from outside' }));

    expect(screen.getByRole('searchbox', { name: 'Search things' })).toHaveValue('from outside');
  });
});
