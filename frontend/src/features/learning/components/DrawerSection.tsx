import { Box, Typography } from '@mui/material';
import { useId, type ReactNode } from 'react';

/** A titled part of a drawer. */
export function DrawerSection({ title, children }: { title: string; children: ReactNode }) {
  const id = useId();
  return (
    <Box component="section" aria-labelledby={id}>
      <Typography id={id} component="h3" variant="h3" sx={{ mb: 1 }}>
        {title}
      </Typography>
      {children}
    </Box>
  );
}

/** The steps of a lesson or hypothesis: state, actor, reason, time. */
export function StepList({
  label,
  steps,
  formatAt,
}: {
  label: string;
  steps: readonly { rowId: number; state: string; actor: string; reason: string; at: string }[];
  formatAt: (at: string) => string;
}) {
  return (
    <Box component="ol" aria-label={label} sx={{ listStyle: 'none', m: 0, p: 0, display: 'grid', gap: 1.5 }}>
      {steps.map((step) => (
        <Box key={step.rowId} component="li" sx={{ borderLeft: 3, borderColor: 'divider', pl: 1.5, minWidth: 0 }}>
          <Typography variant="body2" sx={{ fontWeight: 700 }}>
            {step.state}{' '}
            <Typography component="span" variant="caption" color="text.secondary">
              by {step.actor}, {formatAt(step.at)}
            </Typography>
          </Typography>
          <Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>
            {step.reason}
          </Typography>
        </Box>
      ))}
    </Box>
  );
}
