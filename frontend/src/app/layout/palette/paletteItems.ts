import type { SvgIconComponent } from '@mui/icons-material';
import ArrowForwardIcon from '@mui/icons-material/ArrowForward';
import DarkModeIcon from '@mui/icons-material/DarkModeOutlined';
import ManageAccountsIcon from '@mui/icons-material/ManageAccountsOutlined';
import PersonIcon from '@mui/icons-material/PersonOutlined';
import type { AccountSummary } from '@/shared/api';
import { navHue, type IconHueName } from '@/theme';
import { navigation } from '../../navigation';

export type PaletteGroup = 'Pages' | 'Accounts' | 'Actions';
export const paletteGroups: readonly PaletteGroup[] = ['Pages', 'Accounts', 'Actions'];

export interface PaletteItem {
  id: string;
  group: PaletteGroup;
  label: string;
  /** Secondary text on the right (section, phase, broker). */
  hint: string;
  /** Extra words that also match. */
  keywords: string;
  icon: SvgIconComponent;
  hue: IconHueName;
  run: () => void;
}

interface Targets {
  accounts: readonly AccountSummary[];
  go: (to: string) => void;
  toggleMode: () => void;
}

/** Everything the palette can open or do, grouped; order here is the order shown. */
export function buildPaletteItems({ accounts, go, toggleMode }: Targets): PaletteItem[] {
  const pages = navigation.flatMap((section) =>
    section.items.map((item): PaletteItem => ({
      id: `page:${item.path}`,
      group: 'Pages',
      label: item.label,
      hint: item.phase ? `${section.title} · coming in ${item.phase}` : section.title,
      keywords: section.title,
      icon: item.icon,
      hue: navHue[item.path] ?? 'slate',
      run: () => {
        go(item.path);
      },
    })),
  );
  const accountItems = accounts.map((account): PaletteItem => ({
    id: `account:${account.id}`,
    group: 'Accounts',
    label: account.name,
    hint: `${account.broker} · ${account.maskedClientId}`,
    keywords: 'account broker',
    icon: PersonIcon,
    hue: 'accounts',
    run: () => {
      go(`/accounts?account=${account.id}`);
    },
  }));
  const actions: PaletteItem[] = [
    {
      id: 'action:theme',
      group: 'Actions',
      label: 'Toggle dark mode',
      hint: 'Theme',
      keywords: 'light theme colour appearance',
      icon: DarkModeIcon,
      hue: 'indigo',
      run: toggleMode,
    },
    {
      id: 'action:my-account',
      group: 'Actions',
      label: 'My account',
      hint: '/account',
      keywords: 'profile password change name email sign-in activity',
      icon: ManageAccountsIcon,
      hue: 'accounts',
      run: () => {
        go('/account');
      },
    },
    ...[
      { path: '/accounts', label: 'Go to Accounts' },
      { path: '/settings', label: 'Go to Settings' },
    ].map(({ path, label }): PaletteItem => ({
      id: `action:go:${path}`,
      group: 'Actions',
      label,
      hint: path,
      keywords: 'open navigate',
      icon: ArrowForwardIcon,
      hue: 'indigo',
      run: () => {
        go(path);
      },
    })),
  ];
  return [...pages, ...accountItems, ...actions];
}

/** Every word typed must appear (case-insensitive) in the label, hint or keywords. Empty query keeps everything. */
export function filterPaletteItems(items: readonly PaletteItem[], query: string): PaletteItem[] {
  const words = query.toLowerCase().split(/\s+/).filter(Boolean);
  if (words.length === 0) {
    return [...items];
  }
  return items.filter((item) => {
    const haystack = `${item.label} ${item.hint} ${item.keywords}`.toLowerCase();
    return words.every((word) => haystack.includes(word));
  });
}
