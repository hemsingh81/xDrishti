import BlockIcon from '@mui/icons-material/BlockOutlined';
import CheckIcon from '@mui/icons-material/CheckCircleOutlined';
import ErrorIcon from '@mui/icons-material/ErrorOutlineOutlined';
import HourglassIcon from '@mui/icons-material/HourglassEmptyOutlined';
import LockIcon from '@mui/icons-material/LockOutlined';
import SendIcon from '@mui/icons-material/SendOutlined';
import ShieldIcon from '@mui/icons-material/ShieldOutlined';
import TrendingIcon from '@mui/icons-material/TrendingUpOutlined';
import type { SvgIconComponent } from '@mui/icons-material';

export type StateGroup = 'pending' | 'working' | 'live' | 'done' | 'stopped';

export interface StateLook {
  word: string;
  icon: SvgIconComponent;
  group: StateGroup;
}

/** P12-01's fourteen order states: an icon and a word each (never colour alone), and the group the blotter filters by. */
const looks: Readonly<Record<string, StateLook>> = {
  planned: { word: 'Planned', icon: HourglassIcon, group: 'pending' },
  armed: { word: 'Armed', icon: HourglassIcon, group: 'pending' },
  checking: { word: 'Checking', icon: HourglassIcon, group: 'pending' },
  submitting: { word: 'Submitting', icon: SendIcon, group: 'working' },
  open: { word: 'Open at the broker', icon: SendIcon, group: 'working' },
  filled: { word: 'Filled', icon: CheckIcon, group: 'live' },
  protected: { word: 'Protected', icon: ShieldIcon, group: 'live' },
  managing: { word: 'Managing', icon: TrendingIcon, group: 'live' },
  closing: { word: 'Closing', icon: TrendingIcon, group: 'live' },
  closed: { word: 'Closed', icon: LockIcon, group: 'done' },
  reconciled: { word: 'Reconciled', icon: LockIcon, group: 'done' },
  rejected: { word: 'Rejected', icon: BlockIcon, group: 'stopped' },
  cancelled: { word: 'Cancelled', icon: BlockIcon, group: 'stopped' },
  failed: { word: 'Failed', icon: ErrorIcon, group: 'stopped' },
};

export const stateGroups: readonly StateGroup[] = ['pending', 'working', 'live', 'done', 'stopped'];

/** The look of a state word; `null` for a word outside the vocabulary (the caller shows "Unknown state: X" in the error style). */
export const stateLook = (state: string): StateLook | null => looks[state] ?? null;

/** Who caused an order, in the owner's words. */
export const causeWord = (by: string): string =>
  by === 'tap'
    ? 'Your tap'
    : by === 'auto'
      ? 'Automatic'
      : by === 'system'
        ? 'System'
        : by === 'owner'
          ? 'Owner typed'
          : by;

export const modeWord = (mode: string): string => mode.replace(/_/g, ' ');
