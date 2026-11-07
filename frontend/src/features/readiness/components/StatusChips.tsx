import BlockIcon from '@mui/icons-material/BlockOutlined';
import CancelIcon from '@mui/icons-material/CancelOutlined';
import CheckIcon from '@mui/icons-material/CheckCircleOutlined';
import HourglassIcon from '@mui/icons-material/HourglassEmptyOutlined';
import InfoIcon from '@mui/icons-material/InfoOutlined';
import { Chip } from '@mui/material';
import { labelKind, rowStatus, rowStatusWords } from '../lib/words';

const rowLooks = {
  pass: { color: 'success', Icon: CheckIcon },
  fail: { color: 'error', Icon: CancelIcon },
  // Not available is never green and never "ok": a block icon and the words say it counts as a fail.
  unavailable: { color: 'warning', Icon: BlockIcon },
} as const;

/** One checklist row's status as an icon and words; colour is an addition. */
export function RowStatusChip({ status }: { status: string }) {
  const kind = rowStatus(status);
  const { color, Icon } = rowLooks[kind];
  return (
    <Chip size="small" variant="outlined" color={color} icon={<Icon aria-hidden />} label={rowStatusWords[kind]} />
  );
}

const labelLooks = {
  not_ready: { color: 'error', Icon: CancelIcon },
  building: { color: 'warning', Icon: HourglassIcon },
  usable: { color: 'info', Icon: InfoIcon },
  ready: { color: 'success', Icon: CheckIcon },
  other: { color: 'default', Icon: InfoIcon },
} as const;

/** The readiness label (four of them) in the server's words, with an icon. */
export function LabelChip({ code, text }: { code: string; text: string }) {
  const { color, Icon } = labelLooks[labelKind(code)];
  return <Chip size="small" variant="outlined" color={color} icon={<Icon aria-hidden />} label={text} />;
}

/** A gate: passes or does not, in words. */
export function GateChip({ name, passed }: { name: string; passed: boolean }) {
  return (
    <Chip
      size="small"
      variant="outlined"
      color={passed ? 'success' : 'error'}
      icon={passed ? <CheckIcon aria-hidden /> : <CancelIcon aria-hidden />}
      label={`${name}: ${passed ? 'passes' : 'does not pass'}`}
    />
  );
}
