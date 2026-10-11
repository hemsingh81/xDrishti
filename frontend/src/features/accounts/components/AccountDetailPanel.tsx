import { Box, Card, CardContent, Stack, Tab, Tabs, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ErrorState, LoadingState } from '@/shared/ui';
import { accountQuery } from '../api/accountsQueries';
import { accountTabs, type AccountTab } from '../lib/tabs';
import { ActivityTab } from './ActivityTab';
import { ConnectionStatus } from './ConnectionStatus';
import { ConnectionTab } from './ConnectionTab';
import { DataTab } from './DataTab';
import { OverviewTab } from './OverviewTab';
import { PortfolioTab } from './PortfolioTab';
import { RoleChips } from './RoleChips';
import { RolesTab } from './RolesTab';
import { TradingTab } from './TradingTab';

interface AccountDetailPanelProps {
  accountId: string;
  tab: AccountTab;
  onTabChange: (tab: AccountTab) => void;
  onRemoved: () => void;
}

/** Detail of the selected account: header and the seven tabs (the selected tab is part of the URL). */
export function AccountDetailPanel({ accountId, tab, onTabChange, onRemoved }: AccountDetailPanelProps) {
  const { data: account, error, isPending } = useQuery(accountQuery(accountId));

  if (isPending) {
    return <LoadingState label="Loading account" />;
  }
  if (error) {
    return <ErrorState error={error} />;
  }
  const toRoles = () => {
    onTabChange('roles');
  };

  return (
    <Card component="section" aria-label={`Account ${account.name}`}>
      <CardContent sx={{ pb: 0 }}>
        <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center', flexWrap: 'wrap', rowGap: 1 }}>
          <Box aria-hidden sx={{ width: 12, height: 12, borderRadius: '50%', flex: 'none', bgcolor: account.color }} />
          <Typography variant="h2" component="h2">
            {account.name}
          </Typography>
          <RoleChips roles={account.roles} />
          <Box sx={{ ml: { sm: 'auto' } }}>
            <ConnectionStatus connection={account.connection} />
          </Box>
        </Stack>
      </CardContent>
      <CardContent sx={{ pb: 0, pt: 1 }}>
        <Tabs
          value={tab}
          onChange={(_event, value: AccountTab) => {
            onTabChange(value);
          }}
          variant="scrollable"
          scrollButtons="auto"
          allowScrollButtonsMobile
          aria-label="Account sections"
          sx={{ mt: 1, borderBottom: 1, borderColor: 'divider' }}
        >
          {accountTabs.map(({ key, label }) => (
            <Tab key={key} value={key} label={label} id={`account-tab-${key}`} aria-controls={`account-panel-${key}`} />
          ))}
        </Tabs>
      </CardContent>
      <CardContent role="tabpanel" id={`account-panel-${tab}`} aria-labelledby={`account-tab-${tab}`}>
        {tab === 'overview' && <OverviewTab account={account} />}
        {tab === 'roles' && <RolesTab account={account} onNavigate={onTabChange} />}
        {tab === 'trading' && <TradingTab account={account} onEnableRole={toRoles} />}
        {tab === 'portfolio' && <PortfolioTab account={account} onEnableRole={toRoles} />}
        {tab === 'data' && <DataTab account={account} onEnableRole={toRoles} />}
        {tab === 'connection' && <ConnectionTab account={account} onRemoved={onRemoved} />}
        {tab === 'activity' && <ActivityTab accountId={account.id} />}
      </CardContent>
    </Card>
  );
}
