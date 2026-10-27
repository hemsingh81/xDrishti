import AccountBalanceIcon from '@mui/icons-material/AccountBalanceOutlined';
import AutoAwesomeIcon from '@mui/icons-material/AutoAwesomeOutlined';
import ChatIcon from '@mui/icons-material/ChatOutlined';
import BarChartIcon from '@mui/icons-material/BarChartOutlined';
import ChecklistIcon from '@mui/icons-material/ChecklistOutlined';
import DnsIcon from '@mui/icons-material/DnsOutlined';
import FactCheckIcon from '@mui/icons-material/FactCheckOutlined';
import HistoryIcon from '@mui/icons-material/HistoryOutlined';
import ListAltIcon from '@mui/icons-material/ListAltOutlined';
import MonitorHeartIcon from '@mui/icons-material/MonitorHeartOutlined';
import PeopleIcon from '@mui/icons-material/PeopleOutlined';
import PieChartIcon from '@mui/icons-material/PieChartOutlined';
import PsychologyIcon from '@mui/icons-material/PsychologyOutlined';
import ScienceIcon from '@mui/icons-material/ScienceOutlined';
import ShoppingBasketIcon from '@mui/icons-material/ShoppingBasketOutlined';
import StorageIcon from '@mui/icons-material/StorageOutlined';
import TuneIcon from '@mui/icons-material/TuneOutlined';
import WbSunnyIcon from '@mui/icons-material/WbSunnyOutlined';
import type { SvgIconComponent } from '@mui/icons-material';

/** Names a badge source the shell provides (see layout/navBadges); keeps this list free of feature code. */
export type NavBadgeId = 'accounts-attention';

export interface NavItem {
  path: string;
  label: string;
  icon: SvgIconComponent;
  /** Roadmap phase that delivers the screen; undefined = available now. */
  phase?: string;
  /** Optional count badge on the item; the shell resolves the id to live data. */
  badge?: NavBadgeId;
}

export interface NavSection {
  title: string;
  items: NavItem[];
}

/** Navigation mirrors the prototype (docs/prototype). Planned screens show a "coming in phase …" page. */
export const navigation: NavSection[] = [
  { title: 'Overview', items: [{ path: '/', label: 'Today', icon: WbSunnyIcon }] },
  {
    title: 'Decisions',
    items: [
      { path: '/plans', label: 'Plan review', icon: ChecklistIcon },
      { path: '/suggestions', label: 'Suggestions', icon: AutoAwesomeIcon, phase: 'P9' },
    ],
  },
  {
    title: 'Portfolio',
    items: [
      { path: '/portfolio', label: 'Portfolio', icon: PieChartIcon },
      { path: '/paper', label: 'Paper trading', icon: AccountBalanceIcon },
    ],
  },
  {
    title: 'Market',
    items: [
      { path: '/instruments', label: 'Instruments', icon: ListAltIcon },
      { path: '/baskets', label: 'Baskets', icon: ShoppingBasketIcon },
      { path: '/data', label: 'Data & import', icon: StorageIcon },
      { path: '/quality', label: 'Data quality', icon: FactCheckIcon },
    ],
  },
  {
    title: 'Research',
    items: [
      { path: '/learning', label: 'Learning', icon: PsychologyIcon },
      { path: '/reports', label: 'Reports', icon: BarChartIcon },
      { path: '/lab', label: 'Strategy Lab', icon: ScienceIcon },
      { path: '/assistant', label: 'Assistant', icon: ChatIcon },
    ],
  },
  {
    title: 'System',
    items: [
      { path: '/accounts', label: 'Accounts', icon: PeopleIcon, badge: 'accounts-attention' },
      { path: '/services', label: 'Services', icon: DnsIcon },
      { path: '/settings', label: 'Settings', icon: TuneIcon },
      { path: '/audit', label: 'Audit log', icon: HistoryIcon },
      { path: '/system', label: 'System status', icon: MonitorHeartIcon },
    ],
  },
];

export const allNavItems: NavItem[] = navigation.flatMap((s) => s.items);
export const plannedNavItems: NavItem[] = allNavItems.filter((i) => i.phase);

/** Titles of routes that are not sidebar entries (reached from the user menu), so the top bar can still name the page. */
export const routeTitles: Readonly<Record<string, string>> = { '/account': 'My account' };
