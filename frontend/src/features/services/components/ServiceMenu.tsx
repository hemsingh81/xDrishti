import HistoryIcon from '@mui/icons-material/HistoryOutlined';
import MoreIcon from '@mui/icons-material/MoreVert';
import OpenIcon from '@mui/icons-material/OpenInNewOutlined';
import PauseIcon from '@mui/icons-material/PauseOutlined';
import ResumeIcon from '@mui/icons-material/PlayCircleOutlineOutlined';
import SettingsIcon from '@mui/icons-material/SettingsOutlined';
import TuneIcon from '@mui/icons-material/TuneOutlined';
import { IconButton, ListItemIcon, ListItemText, Menu, MenuItem, Tooltip } from '@mui/material';
import { useId, useState, type ReactNode } from 'react';
import { Link } from 'react-router';
import type { ServiceView } from '@/shared/api';
import { hasSettings } from '../lib/serviceLook';
import type { ServiceActionHandlers } from './ServiceActions';

interface ServiceMenuProps extends Omit<ServiceActionHandlers, 'onRun'> {
  service: ServiceView;
  /** An action for this service is in flight. */
  busy: boolean;
}

function Item({ icon, children }: { icon: ReactNode; children: ReactNode }) {
  return (
    <>
      <ListItemIcon>{icon}</ListItemIcon>
      <ListItemText>{children}</ListItemText>
    </>
  );
}

/**
 * The overflow menu of a service card ("Actions for <service>"): Configure, Pause / Resume and History; for the account
 * sync, Settings… and Manage in Accounts. The keyboard works as in any menu (Enter or arrows open it, arrows move,
 * Escape closes and returns to the button). An action that opens a dialog or a drawer runs once the menu has closed, so
 * closing that dialog brings the focus back to the button.
 */
export function ServiceMenu({ service, busy, onConfigure, onTogglePause, onHistory, onSettings }: ServiceMenuProps) {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const [after, setAfter] = useState<(() => void) | null>(null);
  const buttonId = useId();
  const settings = hasSettings(service);
  if (service.readOnly && !settings && !service.manageUrl) return null;

  // A dialog or drawer opens once the menu has closed (`after`); pausing needs no focus, so it goes at once.
  const choose =
    (action: () => void, now = false) =>
    () => {
      if (now) action();
      else setAfter(() => action);
      setAnchor(null);
    };
  const label = `Actions for ${service.name}`;

  return (
    <>
      <Tooltip title="More actions">
        <IconButton
          id={buttonId}
          size="small"
          aria-label={label}
          aria-haspopup="menu"
          aria-expanded={anchor ? 'true' : 'false'}
          aria-controls={anchor ? `${buttonId}-menu` : undefined}
          onClick={(event) => {
            setAnchor(event.currentTarget);
          }}
        >
          <MoreIcon />
        </IconButton>
      </Tooltip>
      <Menu
        id={`${buttonId}-menu`}
        anchorEl={anchor}
        open={anchor !== null}
        onClose={() => {
          setAnchor(null);
        }}
        slotProps={{
          list: { 'aria-labelledby': buttonId, dense: true },
          transition: {
            onExited: () => {
              if (after) {
                setAfter(null);
                after();
              }
            },
          },
        }}
      >
        {!service.readOnly && [
          <MenuItem
            key="configure"
            aria-haspopup="dialog"
            onClick={choose(() => {
              onConfigure(service);
            })}
          >
            <Item icon={<TuneIcon fontSize="small" />}>Configure</Item>
          </MenuItem>,
          <MenuItem
            key="pause"
            disabled={busy}
            onClick={choose(() => {
              onTogglePause(service);
            }, true)}
          >
            <Item icon={service.enabled ? <PauseIcon fontSize="small" /> : <ResumeIcon fontSize="small" />}>
              {service.enabled ? 'Pause' : 'Resume'}
            </Item>
          </MenuItem>,
          <MenuItem
            key="history"
            aria-haspopup="dialog"
            onClick={choose(() => {
              onHistory(service);
            })}
          >
            <Item icon={<HistoryIcon fontSize="small" />}>History</Item>
          </MenuItem>,
        ]}
        {settings && (
          <MenuItem
            aria-haspopup="dialog"
            onClick={choose(() => {
              onSettings(service);
            })}
          >
            <Item icon={<SettingsIcon fontSize="small" />}>Settings…</Item>
          </MenuItem>
        )}
        {service.readOnly && service.manageUrl && (
          <MenuItem
            component={Link}
            to={service.manageUrl}
            onClick={() => {
              setAnchor(null);
            }}
          >
            <Item icon={<OpenIcon fontSize="small" />}>Manage in Accounts</Item>
          </MenuItem>
        )}
      </Menu>
    </>
  );
}
