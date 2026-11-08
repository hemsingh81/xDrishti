import CancelIcon from '@mui/icons-material/CancelOutlined';
import TrendingFlatIcon from '@mui/icons-material/TrendingFlat';
import HelpIcon from '@mui/icons-material/HelpOutlineOutlined';
import VisibilityIcon from '@mui/icons-material/VisibilityOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Box, Chip, Typography } from '@mui/material';

const states = {
  monitoring: { label: 'monitoring', color: 'default', Icon: VisibilityIcon },
  on_track: { label: 'on track', color: 'default', Icon: TrendingFlatIcon },
  caution: { label: 'caution', color: 'warning', Icon: WarningIcon },
  failure: { label: 'failure', color: 'error', Icon: CancelIcon },
} as const;

type Known = keyof typeof states;
const isKnown = (state: string | null): state is Known => state !== null && Object.hasOwn(states, state);

/** A sequential-test state as an icon and the word; an unknown or missing state reads "unavailable". A flag, never a pass. */
export function SequentialState({ state, series }: { state: string | null; series: string }) {
  const { label, color, Icon } = isKnown(state)
    ? states[state]
    : ({ label: 'unavailable', color: 'default', Icon: HelpIcon } as const);
  return (
    <Chip size="small" variant="outlined" color={color} icon={<Icon aria-hidden />} label={`${series}: ${label}`} />
  );
}

/** The two states and the one line, with the standing note that it is a flag and never a pass. */
export function SequentialBlock({
  edgeState,
  behaviouralState,
  line,
}: {
  edgeState: string | null;
  behaviouralState: string | null;
  line: string | null;
}) {
  return (
    <>
      <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap', mb: 0.5 }}>
        <SequentialState state={edgeState} series="Edge" />
        <SequentialState state={behaviouralState} series="Behavioural" />
      </Box>
      <Typography>{line}</Typography>
      <Typography color="text.secondary" variant="body2">
        A flag only, never a pass: the fixed P10 gates decide.
      </Typography>
    </>
  );
}
