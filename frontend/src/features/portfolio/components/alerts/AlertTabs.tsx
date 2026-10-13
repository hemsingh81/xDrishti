import { formatNumber } from '@/shared/lib/format';
import { ButtonTabs, type ButtonTab } from '@/shared/ui/ButtonTabs';
import { alertPanelId, alertStatusLabels, alertStatuses, alertTabId, type AlertStatus } from '../../lib/alerts';

interface AlertTabsProps {
  value: AlertStatus;
  /** How many alerts each tab holds; undefined while that is not known. */
  counts: Record<AlertStatus, number | undefined>;
  onChange: (tab: AlertStatus) => void;
}

/**
 * Active · Triggered · Cancelled, each with its count, as the shared button tab list (plain buttons rather than MUI `Tabs`,
 * measured: with `Tabs` in this lazy panel the bundler hoists the shared Menu/Popover code into the first-load chunk).
 */
export function AlertTabs({ value, counts, onChange }: AlertTabsProps) {
  const tabs: ButtonTab<AlertStatus>[] = alertStatuses.map((status) => {
    const count = counts[status];
    return {
      value: status,
      label: count === undefined ? alertStatusLabels[status] : `${alertStatusLabels[status]} (${formatNumber(count)})`,
      id: alertTabId(status),
      panelId: alertPanelId(status),
    };
  });
  return <ButtonTabs tabs={tabs} value={value} onChange={onChange} label="Alert status" />;
}
