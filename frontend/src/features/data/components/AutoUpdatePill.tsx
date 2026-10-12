import PauseIcon from '@mui/icons-material/PauseCircleOutlineOutlined';
import ScheduleIcon from '@mui/icons-material/ScheduleOutlined';
import { Box, ButtonBase, Popover } from '@mui/material';
import { useId, useState } from 'react';
import type { DhanOverview, EodOverview } from '@/shared/api';
import { autoLabel, lastRunLook } from '../lib/eod';
import { AutomaticUpdateDetails } from './AutomaticUpdateDetails';
import { runIcons, toneColour } from './autoUpdateLook';
import { pillSx, type PillTone } from './ribbonStyle';

/** What is said after the schedule when the last automatic run did not simply succeed. */
const suffixes: Readonly<Record<string, string>> = {
  failed: 'last run failed',
  skipped: 'last run skipped',
  running: 'running now',
};

/**
 * "Auto 15:50 · next Tue 13 Oct" in the ribbon. A run that failed or was skipped is said after it (word and icon, not
 * colour alone); the full line (when it runs, how the last run ended, why it failed, Reconnect) is in the popover.
 */
export function AutoUpdatePill({ eod, dhan }: { eod: EodOverview; dhan: DhanOverview }) {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const id = useId();
  const run = eod.lastRun;
  const look = run ? lastRunLook(run) : null;
  const Icon = !eod.enabled ? PauseIcon : run ? (runIcons[run.status] ?? ScheduleIcon) : ScheduleIcon;
  const tone: PillTone = !eod.enabled || !look ? 'neutral' : look.tone === 'success' ? 'neutral' : look.tone;
  const suffix = eod.enabled && run ? (suffixes[run.status] ?? null) : null;
  const iconColour = !eod.enabled || !look ? 'text.secondary' : toneColour[look.tone];

  return (
    <>
      <ButtonBase
        aria-haspopup="dialog"
        aria-expanded={anchor ? 'true' : undefined}
        aria-controls={anchor ? id : undefined}
        onClick={(event) => {
          setAnchor(event.currentTarget);
        }}
        sx={[pillSx(tone), { cursor: 'pointer', '&:hover': { borderColor: 'primary.main' } }]}
      >
        <Icon fontSize="small" aria-hidden sx={{ color: iconColour }} />
        <Box component="span" sx={{ fontWeight: 600 }}>
          {suffix ? `${autoLabel(eod)} · ${suffix}` : autoLabel(eod)}
        </Box>
      </ButtonBase>
      <Popover
        open={anchor !== null}
        anchorEl={anchor}
        onClose={() => {
          setAnchor(null);
        }}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'left' }}
        slotProps={{ paper: { id, role: 'dialog', 'aria-label': 'Automatic update', sx: { maxWidth: 440, p: 1.5 } } }}
      >
        <AutomaticUpdateDetails eod={eod} dhan={dhan} />
      </Popover>
    </>
  );
}
