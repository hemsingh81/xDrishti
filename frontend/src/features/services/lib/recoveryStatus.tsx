import BlockIcon from '@mui/icons-material/Block';
import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import ErrorIcon from '@mui/icons-material/ErrorOutlined';
import HelpIcon from '@mui/icons-material/HelpOutlined';
import HourglassIcon from '@mui/icons-material/HourglassEmpty';
import RemoveIcon from '@mui/icons-material/RemoveCircleOutlined';
import SyncIcon from '@mui/icons-material/Sync';
import WarningIcon from '@mui/icons-material/WarningAmber';
import type { SvgIconComponent } from '@mui/icons-material';

export interface RecoveryStatusInfo {
  label: string;
  icon: SvgIconComponent;
  /** Theme palette path of the icon colour; the label always says the status as well. */
  color: string;
  /** Needs the owner's eye (everything but recovered and superseded). */
  attention: boolean;
  /** Said when the server sent no reason. */
  fallback: string;
}

const info = (
  label: string,
  icon: SvgIconComponent,
  color: string,
  attention: boolean,
  fallback: string,
): RecoveryStatusInfo => ({ label, icon, color, attention, fallback });

const statuses: Record<string, RecoveryStatusInfo> = {
  missed: info('Missed', WarningIcon, 'warning.main', true, 'This was not run and has not been recovered yet.'),
  recovered: info('Recovered', CheckCircleIcon, 'success.main', false, 'Everything was filled in.'),
  partial: info('Partly recovered', WarningIcon, 'warning.main', true, 'Only some of the days were filled in.'),
  still_failing: info('Still failing', ErrorIcon, 'error.main', true, 'Recovery was tried and failed.'),
  blocked: info('Blocked', BlockIcon, 'error.main', true, 'Something it depends on has to be recovered first.'),
  waiting: info('Waiting', HourglassIcon, 'text.secondary', true, 'Waiting for a service it depends on.'),
  running: info('Running', SyncIcon, 'info.main', true, 'Recovery is running now.'),
  not_recoverable: info(
    'Cannot be recovered',
    RemoveIcon,
    'text.secondary',
    true,
    'This cannot be filled in afterwards.',
  ),
  superseded: info('Replaced by a later run', CheckCircleIcon, 'text.secondary', false, 'A later run covers this.'),
  out_of_reach: info('Too old to recover', RemoveIcon, 'text.secondary', true, 'The source no longer has this day.'),
};

const unavailable = info('Unavailable', HelpIcon, 'text.secondary', true, 'The status could not be read.');

/** Label, icon and wording of a status. A status this screen does not know reads as unavailable. */
export const recoveryStatus = (status: string): RecoveryStatusInfo => statuses[status] ?? unavailable;
