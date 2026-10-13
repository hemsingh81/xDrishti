import HistoryIcon from '@mui/icons-material/HistoryOutlined';
import PauseIcon from '@mui/icons-material/PauseOutlined';
import PlayIcon from '@mui/icons-material/PlayArrowOutlined';
import ResumeIcon from '@mui/icons-material/PlayCircleOutlineOutlined';
import TuneIcon from '@mui/icons-material/TuneOutlined';
import { Button, Link as MuiLink, Stack } from '@mui/material';
import { Link } from 'react-router';
import type { ServiceView } from '@/shared/api';

export interface ServiceActionHandlers {
  onRun: (service: ServiceView) => void;
  onConfigure: (service: ServiceView) => void;
  onTogglePause: (service: ServiceView) => void;
  onHistory: (service: ServiceView) => void;
}

interface ServiceActionsProps extends ServiceActionHandlers {
  service: ServiceView;
  /** An action for this service is in flight. */
  busy: boolean;
  align?: 'flex-start' | 'flex-end';
}

/**
 * Run now · Configure · Pause / Resume · History. Each button names its service (a table has many rows). An entry that
 * is set elsewhere (the per-account sync) has none of them, only a link to where it is set.
 */
export function ServiceActions({
  service,
  busy,
  align = 'flex-end',
  onRun,
  onConfigure,
  onTogglePause,
  onHistory,
}: ServiceActionsProps) {
  if (service.readOnly) {
    return service.manageUrl ? (
      <MuiLink
        component={Link}
        to={service.manageUrl}
        variant="body2"
        aria-label={`Manage in Accounts: ${service.name}`}
      >
        Manage in Accounts
      </MuiLink>
    ) : null;
  }
  const running = service.status === 'running';
  return (
    <Stack direction="row" useFlexGap sx={{ gap: 0.75, flexWrap: 'wrap', justifyContent: align, minWidth: 0 }}>
      <Button
        size="small"
        variant="outlined"
        startIcon={<PlayIcon />}
        disabled={running || busy}
        aria-label={`Run now: ${service.name}`}
        onClick={() => {
          onRun(service);
        }}
      >
        Run now
      </Button>
      <Button
        size="small"
        variant="contained"
        startIcon={<TuneIcon />}
        aria-haspopup="dialog"
        aria-label={`Configure: ${service.name}`}
        onClick={() => {
          onConfigure(service);
        }}
      >
        Configure
      </Button>
      <Button
        size="small"
        variant="outlined"
        startIcon={service.enabled ? <PauseIcon /> : <ResumeIcon />}
        disabled={busy}
        aria-label={`${service.enabled ? 'Pause' : 'Resume'}: ${service.name}`}
        onClick={() => {
          onTogglePause(service);
        }}
      >
        {service.enabled ? 'Pause' : 'Resume'}
      </Button>
      <Button
        size="small"
        variant="outlined"
        startIcon={<HistoryIcon />}
        aria-haspopup="dialog"
        aria-label={`History: ${service.name}`}
        onClick={() => {
          onHistory(service);
        }}
      >
        History
      </Button>
    </Stack>
  );
}
