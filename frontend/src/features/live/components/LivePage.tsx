import GavelIcon from '@mui/icons-material/GavelOutlined';
import { Suspense } from 'react';
import { useUrlState } from '@/shared/analytics';
import { LoadingState, PageHeader } from '@/shared/ui';
import { ButtonTabs, type ButtonTab } from '@/shared/ui/ButtonTabs';
import { DEFAULT_TAB, tabs, type TabId } from '../lib/tabs';

const ids = tabs.map((tab) => tab.id);
const buttons: readonly ButtonTab<TabId>[] = tabs.map((tab) => ({
  value: tab.id,
  label: tab.label,
  id: `live-tab-${tab.id}`,
  panelId: 'live-panel',
}));

/** Live trading: the small live phase. Orders are typed by hand in Dhan's own app; nothing here places one. */
export function LivePage() {
  const url = useUrlState();
  const tab: TabId = url.choice('tab', ids, DEFAULT_TAB);
  const active = tabs.find((candidate) => candidate.id === tab);
  return (
    <>
      <PageHeader
        title="Live trading"
        icon={GavelIcon}
        hue="portfolio"
        description="The small live phase. Orders are placed by hand in Dhan's own app; nothing here places an order."
      />
      <ButtonTabs
        tabs={buttons}
        value={tab}
        onChange={(next) => {
          url.update({ tab: next === DEFAULT_TAB ? null : next });
        }}
        label="Live trading sections"
      />
      <div role="tabpanel" id="live-panel" aria-labelledby={`live-tab-${tab}`}>
        <Suspense fallback={<LoadingState label="Loading the tab…" />}>{active && <active.Component />}</Suspense>
      </div>
    </>
  );
}
