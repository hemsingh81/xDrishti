import ChecklistIcon from '@mui/icons-material/ChecklistOutlined';
import { Box, Tab, Tabs } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useUrlState } from '@/shared/analytics';
import { ErrorState, LoadingState, PageHeader } from '@/shared/ui';
import { executionStatusQuery, pollIntervalMs } from '../api/statusApi';
import { overviewQuery } from '../api/viewsApi';
import { ActivityTab } from './ActivityTab';
import { EvidenceFoot } from './EvidenceFoot';
import { ModesTab } from './ModesTab';
import { OrdersTab } from './OrdersTab';
import { CanaryBanner, ProtectionBanner, StaleView } from './PageBanners';
import { PositionsTab } from './PositionsTab';
import { TodayTab } from './TodayTab';

const tabs = [
  ['today', 'Today'],
  ['orders', 'Orders'],
  ['positions', 'Positions'],
  ['activity', 'Activity'],
  ['modes', 'Modes'],
] as const;
type TabId = (typeof tabs)[number][0];
const tabIds = tabs.map(([id]) => id);

/** Execution: one page, five tabs (?tab=today|orders|positions|activity|modes), the tab in the address. */
export function ExecutionPage() {
  const url = useUrlState();
  const tab: TabId = url.choice('tab', tabIds, 'today');
  const status = useQuery(executionStatusQuery);
  const overview = useQuery({
    ...overviewQuery(),
    refetchInterval: pollIntervalMs(status.data, false),
  });
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = setInterval(() => {
      setNow(Date.now());
    }, 5000);
    return () => {
      clearInterval(id);
    };
  }, []);
  return (
    <>
      <PageHeader
        title="Execution"
        icon={ChecklistIcon}
        hue="system"
        description="Whether execution can act today, what it did, and what is waiting for you. Nothing is ready until a check says so."
      />
      {status.isPending && <LoadingState label="Loading execution status" />}
      {status.error && <ErrorState error={status.error} />}
      <ProtectionBanner />
      <CanaryBanner pending={overview.data?.canaryPending} />
      <StaleView updatedAt={overview.dataUpdatedAt} marketOpen={status.data?.marketOpen === true} now={now} />
      <Tabs
        value={tab}
        onChange={(_, value: TabId) => {
          url.update({ tab: value === 'today' ? null : value, page: null });
        }}
        aria-label="Execution views"
        variant="scrollable"
        scrollButtons="auto"
        sx={{ mb: 2, borderBottom: 1, borderColor: 'divider' }}
      >
        {tabs.map(([id, label]) => (
          <Tab key={id} value={id} label={label} id={`exec-tab-${id}`} aria-controls="exec-panel" />
        ))}
      </Tabs>
      <Box role="tabpanel" id="exec-panel" aria-labelledby={`exec-tab-${tab}`}>
        {tab === 'today' && <TodayTab />}
        {tab === 'orders' && <OrdersTab />}
        {tab === 'positions' && <PositionsTab />}
        {tab === 'activity' && <ActivityTab />}
        {tab === 'modes' && <ModesTab />}
      </Box>
      <EvidenceFoot limits={overview.data?.limits} date={overview.data?.date} />
    </>
  );
}
