import { Stack } from '@mui/material';
import type { ActionItem } from '@/shared/api';
import { groupBySeverity, type RowActionKey } from '../../lib/actions';
import { ActionSection } from './ActionSection';

export interface ActionListProps {
  items: readonly ActionItem[];
  /** A phone: each item's buttons fold into an Actions menu. */
  phone: boolean;
  onAction: (item: ActionItem, action: RowActionKey) => void;
  onOpenHolding: (item: ActionItem) => void;
}

/** The items by severity (High · Medium · Info): a heading with the count, then a list of items. */
export function ActionList({ items, phone, onAction, onOpenHolding }: ActionListProps) {
  return (
    <Stack spacing={2}>
      {groupBySeverity(items).map((group) => (
        <ActionSection
          key={group.severity}
          group={group}
          phone={phone}
          onAction={onAction}
          onOpenHolding={onOpenHolding}
        />
      ))}
    </Stack>
  );
}
