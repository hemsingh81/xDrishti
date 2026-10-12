import { Box, ButtonBase, Typography } from '@mui/material';
import type { SvgIconComponent } from '@mui/icons-material';
import { useId, type ReactNode } from 'react';
import { formatIstDayAndTime } from '@/shared/lib/format';
import { IconChip } from '@/shared/ui';
import type { IconHueName } from '@/theme';
import { phaseReason } from '../lib/errors';
import { isActive, type PhaseKey } from '../lib/phases';
import { StepProgress } from './StepProgress';
import { StepStatus } from './StepStatus';

interface Phase {
  status: string;
  startedAt: string | null;
  finishedAt: string | null;
  done: number;
  total: number;
  message: string | null;
  code: string | null;
}

interface StepChipProps {
  number: number;
  title: string;
  icon: SvgIconComponent;
  hue: IconHueName;
  /** The step's latest result (or the one in progress); null when it never ran. */
  phase: Phase | null;
  unit: string;
  /** The key figures when the step is at rest: "214 of 214 files". */
  summary: ReactNode;
  /** Short notes after the figures that need the owner ("14 pending", "1 warning"). */
  flags?: readonly string[];
  expanded: boolean;
  onOpen: (anchor: HTMLElement) => void;
  phaseKey: PhaseKey;
}

/**
 * One step of the pipeline in the strip: icon, name, outcome (icon and word), the key figures and when it last ran. While
 * the step runs it shows a determinate progress bar with its counts and what it is doing; a step still queued says so; a
 * failed or skipped one gives the reason in a line. It opens the details popover.
 */
export function StepChip({
  number,
  title,
  icon,
  hue,
  phase,
  unit,
  summary,
  flags = [],
  expanded,
  onOpen,
  phaseKey,
}: StepChipProps) {
  const titleId = useId();
  const at = phase ? (phase.finishedAt ?? phase.startedAt) : null;
  return (
    <Box component="li" aria-labelledby={titleId} data-step={phaseKey} sx={{ listStyle: 'none', minWidth: 0 }}>
      <ButtonBase
        aria-haspopup="dialog"
        aria-expanded={expanded}
        onClick={(event) => {
          onOpen(event.currentTarget);
        }}
        sx={{
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'stretch',
          gap: 0.75,
          width: '100%',
          minHeight: 72,
          px: 2,
          py: 1.25,
          border: 1,
          borderColor: expanded ? 'primary.main' : 'divider',
          borderRadius: 1,
          bgcolor: 'background.paper',
          textAlign: 'left',
          '&:hover': { borderColor: 'primary.main' },
        }}
      >
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, minWidth: 0 }}>
          <IconChip icon={icon} hue={hue} size="sm" />
          <Typography id={titleId} component="span" variant="h4" noWrap sx={{ flex: 1, minWidth: 0 }}>
            {number}. {title}
          </Typography>
          <Box sx={{ flexShrink: 0 }}>
            <StepStatus status={phase?.status ?? null} />
          </Box>
        </Box>
        {phase && isActive(phase.status) ? (
          <StepProgress
            title={title}
            status={phase.status === 'pending' ? 'pending' : 'running'}
            done={phase.done}
            total={phase.total}
            unit={unit}
            message={phase.message}
          />
        ) : (
          <Box
            sx={{
              display: 'flex',
              flexWrap: 'wrap',
              alignItems: 'baseline',
              columnGap: 1.5,
              rowGap: 0.25,
              minWidth: 0,
            }}
          >
            {phase && (phase.status === 'failed' || phase.status === 'skipped') ? (
              <Typography variant="caption" sx={{ color: phase.status === 'failed' ? 'error.main' : 'text.secondary' }}>
                {phaseReason(phase.message, phase.code)}
              </Typography>
            ) : (
              <Typography variant="caption" sx={{ fontWeight: 600 }}>
                {summary}
              </Typography>
            )}
            {flags.map((flag) => (
              <Typography key={flag} variant="caption" sx={{ color: 'warning.main', fontWeight: 600 }}>
                {flag}
              </Typography>
            ))}
            {at && (
              <Typography variant="caption" color="text.secondary">
                <time dateTime={at}>{formatIstDayAndTime(at, { weekday: false })}</time>
              </Typography>
            )}
          </Box>
        )}
      </ButtonBase>
    </Box>
  );
}
