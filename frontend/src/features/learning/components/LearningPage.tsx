import PsychologyIcon from '@mui/icons-material/PsychologyOutlined';
import { useUrlState } from '@/shared/analytics';
import { PageHeader } from '@/shared/ui';
import { ButtonTabs, type ButtonTab } from '@/shared/ui/ButtonTabs';
import { OverviewTab } from './OverviewTab';
import { SetupsTab } from './SetupsTab';

const tabValues = ['overview', 'setups'] as const;
type Tab = (typeof tabValues)[number];

const tabs: readonly ButtonTab<Tab>[] = [
  { value: 'overview', label: 'Overview', id: 'learning-tab-overview', panelId: 'learning-panel' },
  { value: 'setups', label: 'Setups', id: 'learning-tab-setups', panelId: 'learning-panel' },
];

/**
 * Learning (P8-02): what the learning engine holds and which setups (cells) have enough evidence. Read only: the screen has
 * no control that changes a record, starts a job or places anything. The tab, window and open cell live in the URL.
 */
export function LearningPage() {
  const url = useUrlState();
  const tab = url.choice('tab', tabValues, 'overview');
  const select = (next: Tab) => {
    // A cell belongs to the Setups tab; leaving it closes the cell.
    url.update({ tab: next === 'overview' ? null : next, cell: null });
  };
  return (
    <>
      <PageHeader
        title="Learning"
        icon={PsychologyIcon}
        hue="learning"
        description="What the system has learned from past data, read from stored records. About 2.1 years of replayed history: a description of the past, not a forecast."
      />
      <ButtonTabs tabs={tabs} value={tab} onChange={select} label="Learning sections" />
      <div role="tabpanel" id="learning-panel" aria-labelledby={`learning-tab-${tab}`}>
        {tab === 'overview' ? (
          <OverviewTab
            onOpenSetups={() => {
              select('setups');
            }}
          />
        ) : (
          <SetupsTab />
        )}
      </div>
    </>
  );
}
