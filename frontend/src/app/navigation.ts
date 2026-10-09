import AutoAwesomeIcon from '@mui/icons-material/AutoAwesomeOutlined';
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

export interface NavItem {
  path: string;
  label: string;
  icon: SvgIconComponent;
  /** Roadmap phase that delivers the screen; undefined = available now. */
  phase?: string;
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
      { path: '/review', label: 'Plan review', icon: ChecklistIcon, phase: 'P7' },
      { path: '/suggestions', label: 'Suggestions', icon: AutoAwesomeIcon, phase: 'P9' },
    ],
  },
  { title: 'Portfolio', items: [{ path: '/portfolio', label: 'Portfolio', icon: PieChartIcon, phase: 'P2' }] },
  {
    title: 'Market',
    items: [
      { path: '/instruments', label: 'Instruments', icon: ListAltIcon, phase: 'P1' },
      { path: '/baskets', label: 'Baskets', icon: ShoppingBasketIcon, phase: 'P1' },
      { path: '/data', label: 'Data & import', icon: StorageIcon, phase: 'P1' },
      { path: '/quality', label: 'Data quality', icon: FactCheckIcon, phase: 'P1' },
    ],
  },
  {
    title: 'Research',
    items: [
      { path: '/learning', label: 'Learning', icon: PsychologyIcon, phase: 'P6' },
      { path: '/reports', label: 'Reports', icon: BarChartIcon, phase: 'P8' },
      { path: '/lab', label: 'Strategy Lab', icon: ScienceIcon, phase: 'P5' },
    ],
  },
  {
    title: 'System',
    items: [
      { path: '/accounts', label: 'Accounts', icon: PeopleIcon, phase: 'P1' },
      { path: '/services', label: 'Services', icon: DnsIcon, phase: 'P1' },
      { path: '/settings', label: 'Settings', icon: TuneIcon, phase: 'P1' },
      { path: '/audit', label: 'Audit log', icon: HistoryIcon, phase: 'P1' },
      { path: '/system', label: 'System status', icon: MonitorHeartIcon },
    ],
  },
];

export const allNavItems: NavItem[] = navigation.flatMap((s) => s.items);
export const plannedNavItems: NavItem[] = allNavItems.filter((i) => i.phase);
