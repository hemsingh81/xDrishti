import AccountBalanceIcon from '@mui/icons-material/AccountBalanceOutlined';
import { Suspense } from 'react';
import { useUrlState } from '@/shared/analytics';
import { LoadingState, PageHeader } from '@/shared/ui';
import { ButtonTabs, type ButtonTab } from '@/shared/ui/ButtonTabs';
import { DEFAULT_TAB, tabs, type TabId } from '../lib/tabs';

const ids = tabs.map((tab) => tab.id);
const buttons: readonly ButtonTab<TabId>[] = tabs.map((tab) => ({
  value: tab.id,
  label: tab.label,
  id: `paper-tab-${tab.id}`,
  panelId: 'paper-panel',
}));

/** Paper trading: the simulated accounts, derived from stored paper trades. Read only; no order is placed or exists. */
export function PaperPage() {
  const url = useUrlState();
  const tab: TabId = url.choice('tab', ids, DEFAULT_TAB);
  const active = tabs.find((candidate) => candidate.id === tab);
  return (
    <>
      <PageHeader
        title="Paper trading"
        icon={AccountBalanceIcon}
        hue="review"
        description="Simulated trades of the paper accounts, replayed on stored 1-minute bars. No order was placed or exists."
      />
      <ButtonTabs
        tabs={buttons}
        value={tab}
        onChange={(next) => {
          url.update({ tab: next === DEFAULT_TAB ? null : next });
        }}
        label="Paper trading sections"
      />
      <div role="tabpanel" id="paper-panel" aria-labelledby={`paper-tab-${tab}`}>
        <Suspense fallback={<LoadingState label="Loading the tab…" />}>{active && <active.Component />}</Suspense>
      </div>
    </>
  );
}
