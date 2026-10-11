import { screen } from '@testing-library/react';
import { navHue } from '@/theme';
import { renderWithProviders } from '@/test/render';
import { allNavItems } from '../navigation';
import { Sidebar } from './Sidebar';

describe('Sidebar', () => {
  it('shows a coloured icon chip on every item, planned ones included', () => {
    renderWithProviders(<Sidebar />);
    for (const item of allNavItems) {
      const link = screen.getByRole('link', { name: new RegExp(item.label) });
      const chip = link.querySelector('[data-hue]');
      expect(chip, item.label).toHaveAttribute('data-hue', navHue[item.path]);
    }
  });

  it('gives every navigation route a hue', () => {
    for (const item of allNavItems) {
      expect(navHue[item.path], item.label).toBeDefined();
    }
  });

  it('keeps the chip hue on the selected item', () => {
    renderWithProviders(<Sidebar />, { path: '/' });
    const link = screen.getByRole('link', { name: 'Today' });
    expect(link).toHaveAttribute('aria-current', 'page');
    expect(link.querySelector('[data-hue]')).toHaveAttribute('data-hue', 'today');
  });
});
