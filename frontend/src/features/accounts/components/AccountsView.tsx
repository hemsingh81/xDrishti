import { Box } from '@mui/material';
import type { AccountSummary } from '@/shared/api';
import type { AccountTab } from '../lib/tabs';
import { AccountDetailPanel } from './AccountDetailPanel';
import { AccountList } from './AccountList';

interface AccountsViewProps {
  accounts: AccountSummary[];
  selectedId: string;
  tab: AccountTab;
  hrefFor: (id: string) => string;
  onTabChange: (tab: AccountTab) => void;
  onRemoved: () => void;
}

/** List on the left, detail on the right; on phones the list sits above the detail. */
export function AccountsView({ accounts, selectedId, tab, hrefFor, onTabChange, onRemoved }: AccountsViewProps) {
  return (
    <Box
      sx={{
        display: 'grid',
        gap: 2,
        alignItems: 'start',
        gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: 'minmax(260px, 320px) minmax(0, 1fr)' },
      }}
    >
      <AccountList accounts={accounts} selectedId={selectedId} hrefFor={hrefFor} />
      <AccountDetailPanel
        key={selectedId}
        accountId={selectedId}
        tab={tab}
        onTabChange={onTabChange}
        onRemoved={onRemoved}
      />
    </Box>
  );
}
