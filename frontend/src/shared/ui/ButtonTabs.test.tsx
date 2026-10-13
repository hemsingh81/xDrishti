import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { ButtonTabs, type ButtonTab } from './ButtonTabs';

const tabs: ButtonTab<'a' | 'b' | 'c'>[] = ['a', 'b', 'c'].map((value) => ({
  value: value as 'a' | 'b' | 'c',
  label: `Tab ${value.toUpperCase()}`,
  id: `tab-${value}`,
  panelId: `panel-${value}`,
}));

function Harness({ onChange }: { onChange?: (value: string) => void }) {
  const [value, setValue] = useState<'a' | 'b' | 'c'>('a');
  return (
    <ButtonTabs
      tabs={tabs}
      value={value}
      label="Letters"
      onChange={(next) => {
        setValue(next);
        onChange?.(next);
      }}
    />
  );
}

describe('ButtonTabs', () => {
  it('is a named tab list whose selected tab is the one tab stop and controls its panel', () => {
    render(<Harness />);

    expect(screen.getByRole('tablist', { name: 'Letters' })).toBeInTheDocument();
    const a = screen.getByRole('tab', { name: 'Tab A', selected: true });
    expect(a).toHaveAttribute('tabindex', '0');
    expect(a).toHaveAttribute('aria-controls', 'panel-a');
    expect(a).toHaveAttribute('id', 'tab-a');
    expect(screen.getByRole('tab', { name: 'Tab B' })).toHaveAttribute('tabindex', '-1');
  });

  it('selects on click', async () => {
    const seen: string[] = [];
    render(<Harness onChange={(value) => seen.push(value)} />);

    await userEvent.click(screen.getByRole('tab', { name: 'Tab C' }));

    expect(seen).toEqual(['c']);
    expect(screen.getByRole('tab', { name: 'Tab C', selected: true })).toBeInTheDocument();
  });

  it('moves with the arrow keys (wrapping), Home and End, focusing and selecting the new tab', async () => {
    render(<Harness />);
    screen.getByRole('tab', { name: 'Tab A' }).focus();

    await userEvent.keyboard('{ArrowRight}');
    expect(screen.getByRole('tab', { name: 'Tab B', selected: true })).toHaveFocus();
    await userEvent.keyboard('{End}');
    expect(screen.getByRole('tab', { name: 'Tab C', selected: true })).toHaveFocus();
    await userEvent.keyboard('{ArrowRight}');
    expect(screen.getByRole('tab', { name: 'Tab A', selected: true })).toHaveFocus();
    await userEvent.keyboard('{ArrowLeft}');
    expect(screen.getByRole('tab', { name: 'Tab C', selected: true })).toHaveFocus();
    await userEvent.keyboard('{Home}');
    expect(screen.getByRole('tab', { name: 'Tab A', selected: true })).toHaveFocus();
  });

  it('leaves other keys alone', async () => {
    const seen: string[] = [];
    render(<Harness onChange={(value) => seen.push(value)} />);
    screen.getByRole('tab', { name: 'Tab A' }).focus();

    await userEvent.keyboard('x{ArrowDown}');

    expect(seen).toEqual([]);
  });
});
