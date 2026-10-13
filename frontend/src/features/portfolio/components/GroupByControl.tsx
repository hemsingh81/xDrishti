import { Stack, ToggleButton, ToggleButtonGroup, Typography } from '@mui/material';
import { useId } from 'react';
import { groupings, type Grouping } from '../lib/filters';
import { groupingLabels } from '../lib/groups';

/** Group by: None (the flat list) · Account · Bucket · Sector · Basket. A segmented toggle: arrow keys and Tab as for Status. */
export function GroupByControl({ value, onChange }: { value: Grouping; onChange: (groupBy: Grouping) => void }) {
  const labelId = useId();
  return (
    <Stack direction="row" sx={{ alignItems: 'center', gap: 1 }}>
      <Typography id={labelId} variant="body2" color="text.secondary">
        Group by
      </Typography>
      <ToggleButtonGroup
        exclusive
        size="small"
        aria-labelledby={labelId}
        value={value}
        onChange={(_event, next: Grouping | null) => {
          if (next) onChange(next);
        }}
      >
        {groupings.map((grouping) => (
          <ToggleButton key={grouping} value={grouping}>
            {groupingLabels[grouping]}
          </ToggleButton>
        ))}
      </ToggleButtonGroup>
    </Stack>
  );
}
