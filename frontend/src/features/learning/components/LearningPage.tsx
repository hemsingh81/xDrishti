import PsychologyIcon from '@mui/icons-material/PsychologyOutlined';
import { Suspense } from 'react';
import { useUrlState } from '@/shared/analytics';
import { LoadingState, PageHeader } from '@/shared/ui';
import { ButtonTabs, type ButtonTab } from '@/shared/ui/ButtonTabs';
import { DEFAULT_TAB, tabs, type TabId } from '../lib/tabs';

const ids = tabs.map((tab) => tab.id);
const buttons: readonly ButtonTab<TabId>[] = tabs.map((tab) => ({
  value: tab.id,
  label: tab.label,
  id: `learning-tab-${tab.id}`,
  panelId: 'learning-panel',
}));

/**
 * Learning (P8-02): what the learning engine holds and which setups (cells) have enough evidence. Read only: the screen has
 * no control that changes a record, starts a job or places anything. The tab, window and open cell live in the URL.
 */
export function LearningPage() {
  const url = useUrlState();
  // A cell in the address belongs to the Setups tab (the registry id 'setups', checked by `TabId`), so a link with `?cell=`
  // opens that tab even without `tab=setups`.
  const tab: TabId = url.text('cell') === null ? url.choice('tab', ids, DEFAULT_TAB) : 'setups';
  const select = (next: TabId) => {
    // A cell belongs to the Setups tab; leaving it closes the cell.
    url.update({ tab: next === DEFAULT_TAB ? null : next, cell: null });
  };
  const active = tabs.find((candidate) => candidate.id === tab);
  return (
    <>
      <PageHeader
        title="Learning"
        icon={PsychologyIcon}
        hue="learning"
        description="What the system has learned from past data, read from stored records. About 2.1 years of replayed history: a description of the past, not a forecast."
      />
      <ButtonTabs tabs={buttons} value={tab} onChange={select} label="Learning sections" />
      <div role="tabpanel" id="learning-panel" aria-labelledby={`learning-tab-${tab}`}>
        <Suspense fallback={<LoadingState label="Loading the tab…" />}>
          {active && <active.Component open={select} />}
        </Suspense>
      </div>
    </>
  );
}
