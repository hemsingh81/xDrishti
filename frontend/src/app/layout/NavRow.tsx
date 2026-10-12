import { Box, ListItemButton, ListItemIcon, ListItemText, Tooltip } from '@mui/material';
import { NavLink } from 'react-router';
import { IconChip } from '@/shared/ui';
import { navHue, numeric } from '@/theme';
import type { NavItem } from '../navigation';
import type { NavBadge } from './navBadges';

interface NavRowProps {
  item: NavItem;
  badge?: NavBadge | null;
  onNavigate?: () => void;
}

/** One navigation link: icon chip, label and, when `badge` is given, a count bubble (red or amber) with a tooltip. */
export function NavRow({ item, badge = null, onNavigate }: NavRowProps) {
  const { path, label, icon, phase } = item;
  const link = (
    <ListItemButton
      component={NavLink}
      to={path}
      end={path === '/'}
      onClick={onNavigate}
      aria-label={badge ? `${label}, ${badge.label}` : undefined}
      sx={{ mb: 0.25 }}
    >
      <ListItemIcon sx={{ minWidth: 40 }}>
        <IconChip icon={icon} hue={navHue[path] ?? 'slate'} size="sm" />
      </ListItemIcon>
      <ListItemText primary={label} slotProps={{ primary: { sx: { fontWeight: 550, opacity: phase ? 0.7 : 1 } } }} />
      {badge && (
        <Box
          component="span"
          aria-hidden
          sx={{
            ml: 'auto',
            minWidth: 19,
            height: 19,
            px: 0.75,
            borderRadius: '10px',
            display: 'inline-flex',
            alignItems: 'center',
            justifyContent: 'center',
            fontSize: 11,
            fontWeight: 700,
            bgcolor: `${badge.severity}.main`,
            color: `${badge.severity}.contrastText`,
            ...numeric,
          }}
        >
          {badge.count}
        </Box>
      )}
    </ListItemButton>
  );
  if (!badge) {
    return link;
  }
  return (
    <Tooltip
      placement="right"
      describeChild
      title={
        <>
          {badge.lines.map((line) => (
            <Box key={line}>{line}</Box>
          ))}
        </>
      }
    >
      {link}
    </Tooltip>
  );
}
