import { Button, Stack, Tooltip, Typography } from '@mui/material';
import { useId } from 'react';

export interface DisabledAction {
  label: string;
  /** Why it cannot be used yet — shown on hover and as visible text. */
  reason: string;
}

/** Buttons for things that cannot work yet. They are disabled; the reason is always visible, not only on hover. */
export function DisabledActions({ actions }: { actions: DisabledAction[] }) {
  const reasonId = useId();
  const reasons = [...new Set(actions.map((a) => a.reason))];
  return (
    <Stack spacing={0.75}>
      <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 1 }}>
        {actions.map((action) => (
          <Tooltip key={action.label} title={action.reason}>
            <span>
              <Button variant="outlined" disabled aria-describedby={reasonId}>
                {action.label}
              </Button>
            </span>
          </Tooltip>
        ))}
      </Stack>
      <Typography id={reasonId} variant="body2" color="text.secondary">
        {reasons.join(' ')}
      </Typography>
    </Stack>
  );
}
