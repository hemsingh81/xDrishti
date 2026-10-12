import { Box, Stack, Typography } from '@mui/material';
import type { SvgIconComponent } from '@mui/icons-material';
import type { ReactNode } from 'react';
import { formatIstDateTime } from '@/shared/lib/format';
import { IconChip } from '@/shared/ui';
import type { IconHueName } from '@/theme';
import { StepStatus } from './StepStatus';

interface StepDetailsProps {
  number: number;
  title: string;
  icon: SvgIconComponent;
  hue: IconHueName;
  /** The step's latest result; null when it never ran. */
  phase: { status: string; startedAt: string | null; finishedAt: string | null } | null;
  children: ReactNode;
}

/** The body of a step's popover: title, outcome chip, the step's facts and when it last ran. */
export function StepDetails({ number, title, icon, hue, phase, children }: StepDetailsProps) {
  const at = phase ? (phase.finishedAt ?? phase.startedAt) : null;
  return (
    <Stack spacing={1.25}>
      <Stack direction="row" spacing={1.25} sx={{ alignItems: 'center' }}>
        <IconChip icon={icon} hue={hue} size="md" />
        <Typography variant="h3" component="h2" sx={{ flex: 1, minWidth: 0 }}>
          {number}. {title}
        </Typography>
        <Box sx={{ flexShrink: 0 }}>
          <StepStatus status={phase?.status ?? null} />
        </Box>
      </Stack>
      <Box sx={{ display: 'grid', gap: 0.75, minWidth: 0, overflowWrap: 'anywhere' }}>{children}</Box>
      {at && (
        <Typography variant="body2" color="text.secondary">
          {phase?.status === 'running' ? 'Started' : 'Last run'} <time dateTime={at}>{formatIstDateTime(at)}</time>
        </Typography>
      )}
    </Stack>
  );
}
