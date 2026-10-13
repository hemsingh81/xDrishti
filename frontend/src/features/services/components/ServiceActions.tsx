import PlayIcon from '@mui/icons-material/PlayArrowOutlined';
import SettingsIcon from '@mui/icons-material/SettingsOutlined';
import { Button, Stack } from '@mui/material';
import type { ServiceView } from '@/shared/api';
import { hasSettings } from '../lib/serviceLook';
import { ServiceMenu } from './ServiceMenu';

export interface ServiceActionHandlers {
  onRun: (service: ServiceView) => void;
  onConfigure: (service: ServiceView) => void;
  onTogglePause: (service: ServiceView) => void;
  onHistory: (service: ServiceView) => void;
  /** The settings dialog of an entry that has its own (the account sync). */
  onSettings: (service: ServiceView) => void;
}

interface ServiceActionsProps extends ServiceActionHandlers {
  service: ServiceView;
  /** An action for this service is in flight. */
  busy: boolean;
}

/**
 * One primary button and the overflow menu. Run now is the primary one (also on a paused service; off while it runs).
 * The entry that is set elsewhere (the per-account sync) has Settings instead, because it cannot be run by hand. Each
 * button names its service, as there is one set per card.
 */
export function ServiceActions({ service, busy, ...handlers }: ServiceActionsProps) {
  const running = service.status === 'running';
  return (
    <Stack direction="row" sx={{ gap: 0.5, alignItems: 'center', flexShrink: 0 }}>
      {hasSettings(service) ? (
        <Button
          size="small"
          variant="contained"
          startIcon={<SettingsIcon />}
          aria-haspopup="dialog"
          aria-label={`Settings: ${service.name}`}
          onClick={() => {
            handlers.onSettings(service);
          }}
        >
          Settings
        </Button>
      ) : (
        !service.readOnly && (
          <Button
            size="small"
            variant="contained"
            startIcon={<PlayIcon />}
            disabled={running || busy}
            aria-label={`Run now: ${service.name}`}
            onClick={() => {
              handlers.onRun(service);
            }}
          >
            Run now
          </Button>
        )
      )}
      <ServiceMenu service={service} busy={busy} {...handlers} />
    </Stack>
  );
}
