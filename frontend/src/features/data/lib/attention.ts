import type { DataOverview } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import type { DataTab } from './tabs';

export interface AttentionItem {
  id: 'calendar' | 'actions' | 'gaps';
  /** The tab the link opens. */
  tab: DataTab;
  /** Where the link goes (the query string): the tab and the filter of its list. */
  search: string;
  count: number;
  text: string;
}

const noun = (count: number, one: string, many: string) => `${formatNumber(count)} ${count === 1 ? one : many}`;

/** What waits for the owner: calendar proposals, suggested corporate actions and open gaps (only those above zero). */
export function attentionItems(overview: Pick<DataOverview, 'calendarProposals' | 'pendingCorporateActions' | 'gaps'>) {
  const all: AttentionItem[] = [
    {
      id: 'calendar',
      tab: 'calendar',
      search: '?tab=calendar',
      count: overview.calendarProposals,
      text: noun(overview.calendarProposals, 'calendar proposal', 'calendar proposals'),
    },
    {
      id: 'actions',
      tab: 'actions',
      search: '?tab=actions&actions=suggested',
      count: overview.pendingCorporateActions,
      text: noun(overview.pendingCorporateActions, 'suggested corporate action', 'suggested corporate actions'),
    },
    {
      id: 'gaps',
      tab: 'gaps',
      search: '?tab=gaps&gaps=open',
      count: overview.gaps.open,
      text: noun(overview.gaps.open, 'open gap', 'open gaps'),
    },
  ];
  return all.filter((item) => item.count > 0);
}
