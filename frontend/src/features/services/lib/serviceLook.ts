import AccountIcon from '@mui/icons-material/AccountBalanceWalletOutlined';
import ChartIcon from '@mui/icons-material/CandlestickChartOutlined';
import DnsIcon from '@mui/icons-material/DnsOutlined';
import ListIcon from '@mui/icons-material/ListAltOutlined';
import type { SvgIconComponent } from '@mui/icons-material';
import type { IconHueName } from '@/theme';

/** The id of the per-account holdings sync: the one read-only entry, set per account under Accounts. */
export const accountSyncId = 'account-sync';

interface ServiceLook {
  icon: SvgIconComponent;
  hue: IconHueName;
}

const looks: Readonly<Record<string, ServiceLook>> = {
  eod: { icon: ChartIcon, hue: 'data' },
  'instrument-master': { icon: ListIcon, hue: 'instruments' },
  [accountSyncId]: { icon: AccountIcon, hue: 'accounts' },
};

/** The icon chip of a service: its own for the ones we know, the Services icon for any other. */
export const lookOf = (id: string): ServiceLook => looks[id] ?? { icon: DnsIcon, hue: 'services' };

/** The service's own settings dialog: only the account sync has one besides Configure. */
export const hasSettings = (service: { id: string; readOnly: boolean }): boolean =>
  service.readOnly && service.id === accountSyncId;
