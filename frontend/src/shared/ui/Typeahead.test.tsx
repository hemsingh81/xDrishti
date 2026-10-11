import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { AppThemeProvider } from '@/theme';
import { Typeahead, type TypeaheadOption, type TypeaheadProps } from './Typeahead';

const options: TypeaheadOption[] = [
  { value: 'Financial Services', label: 'Financial Services', count: 1_204 },
  { value: 'Information Technology', label: 'Information Technology', count: 98 },
  { value: 'Oil Gas', label: 'Oil Gas & Consumable Fuels', count: 41, hint: 'Energy' },
  { value: 'Unclassified', label: 'Unclassified', count: 3_630 },
  { value: 'odd', label: 'Odd (Co) <b>x</b> .* \\ [a]' },
];

function Harness({ initial = null, onChange, ...props }: Partial<TypeaheadProps> & { initial?: string | null }) {
  const [value, setValue] = useState<string | null>(initial);
  return (
    <AppThemeProvider>
      <Typeahead
        label="Sector"
        placeholder="All sectors"
        options={options}
        {...props}
        value={value}
        onChange={(next) => {
          setValue(next);
          onChange?.(next);
        }}
      />
    </AppThemeProvider>
  );
}

const box = () => screen.getByRole('combobox', { name: 'Sector' });
const optionNames = () => screen.queryAllByRole('option').map((o) => o.getAttribute('aria-label'));

describe('Typeahead', () => {
  it('is a combobox named by its label, with the placeholder and a listbox of options with right-aligned counts', async () => {
    render(<Harness />);

    expect(box()).toHaveAttribute('placeholder', 'All sectors');
    await userEvent.click(box());

    expect(screen.getByRole('listbox')).toBeInTheDocument();
    expect(optionNames()).toEqual([
      'Financial Services 1,204',
      'Information Technology 98',
      'Oil Gas & Consumable Fuels Energy 41',
      'Unclassified 3,630',
      'Odd (Co) <b>x</b> .* \\ [a]',
    ]);
    expect(screen.getByRole('option', { name: 'Unclassified 3,630' })).toBeInTheDocument();
  });

  it('filters as you type (case-insensitive substring) and highlights the match', async () => {
    render(<Harness />);

    await userEvent.type(box(), 'FIN');

    expect(optionNames()).toEqual(['Financial Services 1,204']);
    const option = screen.getByRole('option');
    expect(option.querySelector('mark')?.textContent).toBe('Fin');

    await userEvent.clear(box());
    await userEvent.type(box(), 'ogy');
    expect(optionNames()).toEqual(['Information Technology 98']);
    expect(screen.getByRole('option').querySelector('mark')?.textContent).toBe('ogy');
  });

  it('treats what is typed as plain text: special characters filter and highlight literally', async () => {
    render(<Harness />);

    for (const typed of ['(', '<b>', '.*', '\\', '[a]']) {
      await userEvent.clear(box());
      box().focus();
      await userEvent.paste(typed); // `type` would read `[` and `{` as key descriptions
      expect(optionNames()).toEqual(['Odd (Co) <b>x</b> .* \\ [a]']);
      const mark = screen.getByRole('option').querySelector('mark');
      expect(mark?.textContent).toBe(typed);
      expect(screen.getByRole('option').querySelector('b')).toBeNull(); // no markup came from the label or the query
    }

    await userEvent.clear(box());
    box().focus();
    await userEvent.paste('(x');
    expect(screen.getByText('No matches')).toBeInTheDocument();
  });

  it('filters and highlights by the same rule: spaces around the text and letter case do not matter', async () => {
    render(<Harness />);

    await userEvent.type(box(), '  UNCLASS ');

    expect(optionNames()).toEqual(['Unclassified 3,630']);
    expect(screen.getByRole('option').querySelector('mark')?.textContent).toBe('Unclass');
  });

  it('chooses an option with the mouse and reports its value', async () => {
    const onChange = vi.fn();
    render(<Harness onChange={onChange} />);

    await userEvent.click(box());
    await userEvent.click(screen.getByRole('option', { name: 'Unclassified 3,630' }));

    expect(onChange).toHaveBeenLastCalledWith('Unclassified');
    expect(box()).toHaveValue('Unclassified');
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
  });

  it('works from the keyboard: arrows to move, Enter to choose, Escape to close', async () => {
    const onChange = vi.fn();
    render(<Harness onChange={onChange} />);

    await userEvent.tab();
    expect(box()).toHaveFocus();
    await userEvent.keyboard('{ArrowDown}'); // opens the list
    expect(screen.getByRole('listbox')).toBeInTheDocument();
    await userEvent.keyboard('{ArrowDown}');
    expect(box()).toHaveAttribute('aria-activedescendant', screen.getAllByRole('option')[0]!.id);
    await userEvent.keyboard('{ArrowDown}');
    const second = screen.getByRole('option', { name: /^Information Technology/ });
    expect(box()).toHaveAttribute('aria-activedescendant', second.id);
    await userEvent.keyboard('{Enter}');
    expect(onChange).toHaveBeenLastCalledWith('Information Technology');

    await userEvent.keyboard('{ArrowDown}');
    expect(screen.getByRole('listbox')).toBeInTheDocument();
    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
  });

  it('shows the chosen value and clears it with the clear button', async () => {
    const onChange = vi.fn();
    render(<Harness initial="Oil Gas" onChange={onChange} />);
    expect(box()).toHaveValue('Oil Gas & Consumable Fuels');

    await userEvent.tab(); // the clear button shows on hover / focus
    await userEvent.click(screen.getByRole('button', { name: 'Clear Sector' }));

    expect(onChange).toHaveBeenLastCalledWith(null);
    expect(box()).toHaveValue('');
  });

  it('clears when the text is emptied, and has no clear button when it is not clearable', async () => {
    const onChange = vi.fn();
    const { unmount } = render(<Harness initial="Oil Gas" onChange={onChange} />);
    await userEvent.clear(box());
    expect(onChange).toHaveBeenLastCalledWith(null);
    unmount();

    render(<Harness initial="Oil Gas" clearable={false} />);
    await userEvent.tab();
    expect(screen.queryByRole('button', { name: 'Clear Sector' })).not.toBeInTheDocument();
  });

  it('shows the no-options text when nothing matches', async () => {
    render(<Harness noOptionsText="No sector matches" />);

    await userEvent.type(box(), 'zzz');

    expect(screen.getByText('No sector matches')).toBeInTheDocument();
    expect(screen.queryAllByRole('option')).toHaveLength(0);
  });

  it('keeps a value that is not among the options (no rows right now) selected and visible', async () => {
    render(<Harness initial="Retired Sector" />);

    expect(box()).toHaveValue('Retired Sector');
    await userEvent.click(box());
    expect(screen.getByRole('option', { name: 'Retired Sector' })).toHaveAttribute('aria-selected', 'true');
  });

  it('shows a loading message while the options load', async () => {
    render(<Harness options={[]} loading />);

    await userEvent.click(box());

    expect(screen.getByText('Loading…')).toBeInTheDocument();
  });

  it('disables options', async () => {
    render(<Harness getOptionDisabled={(o) => o.value === 'Unclassified'} />);

    await userEvent.click(box());

    expect(screen.getByRole('option', { name: 'Unclassified 3,630' })).toHaveAttribute('aria-disabled', 'true');
    expect(screen.getByRole('option', { name: 'Financial Services 1,204' })).not.toHaveAttribute(
      'aria-disabled',
      'true',
    );
  });
});
