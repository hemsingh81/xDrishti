import BlockIcon from '@mui/icons-material/Block';
import CheckCircleOutlinedIcon from '@mui/icons-material/CheckCircleOutlined';
import ErrorOutlinedIcon from '@mui/icons-material/ErrorOutlined';
import { Chip } from '@mui/material';
import { words } from '../lib/auditParams';

const outcomes = {
  ok: { icon: <CheckCircleOutlinedIcon />, label: 'Recorded', color: 'default' },
  failed: { icon: <ErrorOutlinedIcon />, label: 'Failed', color: 'error' },
  refused: { icon: <BlockIcon />, label: 'Refused', color: 'warning' },
} as const;

/** The outcome as a word and an icon, never colour alone. "Recorded" means the store holds the row, not that the effect was verified. */
export function OutcomeChip({ outcome }: { outcome: string }) {
  const known = outcome === 'failed' || outcome === 'refused' || outcome === 'ok' ? outcomes[outcome] : undefined;
  return known ? (
    <Chip size="small" variant="outlined" icon={known.icon} label={known.label} color={known.color} />
  ) : (
    <Chip size="small" variant="outlined" label={words(outcome)} />
  );
}

/** The record a row comes from, as a word. */
export function SourceChip({ source }: { source: string }) {
  return <Chip size="small" variant="outlined" label={words(source)} />;
}
