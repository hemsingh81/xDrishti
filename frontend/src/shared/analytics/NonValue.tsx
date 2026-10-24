import BlockIcon from '@mui/icons-material/BlockOutlined';
import ErrorIcon from '@mui/icons-material/ErrorOutlineOutlined';
import HelpIcon from '@mui/icons-material/HelpOutlineOutlined';
import HourglassIcon from '@mui/icons-material/HourglassEmptyOutlined';
import InboxIcon from '@mui/icons-material/InboxOutlined';
import ScaleIcon from '@mui/icons-material/ScaleOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Box, Typography } from '@mui/material';
import type { SvgIconComponent } from '@mui/icons-material';
import { formatCount } from './format';
import type { MetricState } from './metric';

type NonValueState = Exclude<MetricState, { kind: 'value' }>;

interface Look {
  icon: SvgIconComponent;
  word: string;
  /** A theme palette path. Never `success.main`: a missing or caveated figure is not "ok". */
  color: 'text.secondary' | 'warning.main' | 'error.main';
}

const looks: Record<NonValueState['kind'], Look> = {
  provisional: { icon: WarningIcon, word: 'Provisional', color: 'warning.main' },
  insufficient: { icon: ScaleIcon, word: 'Insufficient data', color: 'text.secondary' },
  unavailable: { icon: BlockIcon, word: 'Not available', color: 'text.secondary' },
  not_evaluable: { icon: HelpIcon, word: 'Not evaluable', color: 'text.secondary' },
  empty: { icon: InboxIcon, word: 'No records yet', color: 'text.secondary' },
  loading: { icon: HourglassIcon, word: 'Loading', color: 'text.secondary' },
  error: { icon: ErrorIcon, word: 'Could not load', color: 'error.main' },
};

function detailOf(state: NonValueState): string | undefined {
  if (state.kind === 'insufficient') return `n ${formatCount(state.n)} of at least ${formatCount(state.min)}`;
  return 'reason' in state ? state.reason : undefined;
}

interface NonValueProps {
  state: NonValueState;
  /** Print only the word, for a place that shows the reason elsewhere (a tight chart legend). Default: word and reason. */
  wordOnly?: boolean;
}

/**
 * How every state without a number is shown: an icon, a word, and the reason when there is one. The icon is decoration
 * (`aria-hidden`); the word is the status, so nothing depends on colour. No state here uses the `good` colour.
 */
export function NonValue({ state, wordOnly = false }: NonValueProps) {
  const look = looks[state.kind];
  const detail = wordOnly ? undefined : detailOf(state);
  const Icon = look.icon;
  return (
    <Box
      component="span"
      data-state={state.kind}
      sx={{ display: 'inline-flex', flexDirection: 'column', alignItems: 'inherit', color: look.color }}
    >
      <Box component="span" sx={{ display: 'inline-flex', alignItems: 'center', gap: 0.5 }}>
        <Icon aria-hidden fontSize="inherit" sx={{ fontSize: '1.1em' }} />
        <Typography component="span" variant="inherit" sx={{ fontWeight: 600, color: 'inherit' }}>
          {look.word}
        </Typography>
      </Box>
      {detail && (
        <Typography component="span" variant="caption" sx={{ color: 'text.secondary', lineHeight: 1.4 }}>
          {detail}
        </Typography>
      )}
    </Box>
  );
}
