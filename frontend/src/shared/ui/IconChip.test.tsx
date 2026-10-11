import PeopleIcon from '@mui/icons-material/PeopleOutlined';
import { ThemeProvider } from '@mui/material/styles';
import { render, screen } from '@testing-library/react';
import { createAppTheme, iconChip, type ColorMode } from '@/theme';
import { IconChip } from './IconChip';
import { PageHeader } from './PageHeader';
import { StatTile } from './StatTile';

function renderIn(mode: ColorMode, ui: React.ReactElement) {
  return render(<ThemeProvider theme={createAppTheme(mode)}>{ui}</ThemeProvider>);
}

describe('IconChip', () => {
  it('uses the strong value on a 12 % tint in light mode', () => {
    renderIn('light', <IconChip icon={PeopleIcon} hue="today" label="Today" />);
    expect(screen.getByRole('img', { name: 'Today' })).toHaveStyle({
      color: '#df6714',
      backgroundColor: 'rgba(223, 103, 20, 0.12)',
    });
  });

  it('uses the light value on a 16 % tint in dark mode', () => {
    renderIn('dark', <IconChip icon={PeopleIcon} hue="today" label="Today" />);
    expect(screen.getByRole('img', { name: 'Today' })).toHaveStyle({
      color: '#f59e0b',
      backgroundColor: 'rgba(245, 158, 11, 0.16)',
    });
  });

  it('is decorative (no role, hidden) without a label', () => {
    const { container } = renderIn('light', <IconChip icon={PeopleIcon} hue="accounts" />);
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
    expect(container.querySelector('[data-hue="accounts"]')).toHaveAttribute('aria-hidden', 'true');
  });

  it.each(['sm', 'md', 'lg'] as const)('renders the %s size', (size) => {
    renderIn('light', <IconChip icon={PeopleIcon} hue="indigo" size={size} label="Chip" />);
    expect(screen.getByRole('img', { name: 'Chip' })).toHaveStyle({
      width: `${iconChip[size].box}px`,
      height: `${iconChip[size].box}px`,
    });
  });
});

describe('PageHeader and StatTile with icons', () => {
  it('shows the icon chip before the title when icon and hue are given', () => {
    const { container } = renderIn('light', <PageHeader title="Accounts" icon={PeopleIcon} hue="accounts" />);
    expect(screen.getByRole('heading', { level: 1, name: 'Accounts' })).toBeInTheDocument();
    expect(container.querySelector('[data-hue="accounts"]')).toBeInTheDocument();
  });

  it('shows no chip without an icon', () => {
    const { container } = renderIn('light', <PageHeader title="Plain" />);
    expect(container.querySelector('[data-hue]')).not.toBeInTheDocument();
  });

  it('shows the tile icon chip and keeps label and value', () => {
    const { container } = renderIn('light', <StatTile label="API" value="v1" icon={PeopleIcon} hue="sky" />);
    expect(screen.getByText('API')).toBeInTheDocument();
    expect(screen.getByText('v1')).toBeInTheDocument();
    expect(container.querySelector('[data-hue="sky"]')).toBeInTheDocument();
  });
});
