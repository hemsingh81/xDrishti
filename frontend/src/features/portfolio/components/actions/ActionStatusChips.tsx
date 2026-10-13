import { Chip, Stack } from '@mui/material';
import type { ActionCounts } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { actionStatusLabels, actionStatuses, type ActionStatus } from '../../lib/actions';

interface ActionStatusChipsProps {
  value: ActionStatus;
  counts: ActionCounts | undefined;
  onChange: (status: ActionStatus) => void;
}

/** Open · Snoozed · Done, each with how many items it holds in the scope. Toggle chips: the pressed one is the list shown. */
export function ActionStatusChips({ value, counts, onChange }: ActionStatusChipsProps) {
  return (
    <Stack direction="row" role="group" aria-label="Show items" sx={{ flexWrap: 'wrap', gap: 1 }}>
      {actionStatuses.map((status) => {
        const on = value === status;
        const label = actionStatusLabels[status];
        return (
          <Chip
            key={status}
            clickable
            aria-pressed={on}
            variant={on ? 'filled' : 'outlined'}
            color={on ? 'primary' : 'default'}
            label={counts ? `${label} ${formatNumber(counts[status])}` : label}
            onClick={() => {
              onChange(status);
            }}
          />
        );
      })}
    </Stack>
  );
}
